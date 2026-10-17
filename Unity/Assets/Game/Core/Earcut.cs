using System;
using System.Collections.Generic;

namespace NeonKaido
{
    /// Faithful C# port of mapbox/earcut v3.0.2 as vendored in three
    /// (node_modules/three/src/extras/lib/earcut.js). Used by THREE.ShapeGeometry for every
    /// shape-based mesh (ground sidewalks, buildings). Output = triangle index triples into
    /// the flat data array. JS int semantics (|0) and strict equality preserved.
    public static class Earcut
    {
        class Node
        {
            public int i;          // vertex index in coordinates array
            public double x, y;    // vertex coordinates
            public Node prev, next; // previous and next vertex nodes in a polygon ring
            public double z;       // z-order curve value
            public Node prevZ, nextZ; // previous and next nodes in z-order
            public bool steiner;   // indicates whether this is a steiner point
        }

        public static int[] Triangulate(double[] data, int[] holeIndices, int dim = 2)
        {
            var triangles = new List<int>();

            bool hasHoles = holeIndices != null && holeIndices.Length > 0;
            int outerLen = hasHoles ? holeIndices[0] * dim : data.Length;
            var outerNode = LinkedList(data, 0, outerLen, dim, true);

            if (outerNode == null || outerNode.next == outerNode.prev) return triangles.ToArray();

            double minX = 0, minY = 0, invSize = 0;

            if (hasHoles) outerNode = EliminateHoles(data, holeIndices, outerNode, dim);

            // if the shape is not too simple, we'll use z-order curve hash later; calculate polygon bbox
            if (data.Length > 80 * dim)
            {
                minX = data[0];
                minY = data[1];
                double maxX = minX, maxY = minY;
                for (int i = dim; i < outerLen; i += dim)
                {
                    double x = data[i], y = data[i + 1];
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
                // minX, minY and invSize are later used to transform coords into integers for z-order calculation
                invSize = Math.Max(maxX - minX, maxY - minY);
                invSize = invSize != 0 ? 32767 / invSize : 0;
            }

            EarcutLinked(outerNode, triangles, dim, minX, minY, invSize, 0);

            return triangles.ToArray();
        }

        // create a circular doubly linked list from polygon points in the specified winding order
        static Node LinkedList(double[] data, int start, int end, int dim, bool clockwise)
        {
            Node last = null;

            if (clockwise == (SignedArea(data, start, end, dim) > 0))
            {
                for (int i = start; i < end; i += dim) last = InsertNode(i / dim, data[i], data[i + 1], last);
            }
            else
            {
                for (int i = end - dim; i >= start; i -= dim) last = InsertNode(i / dim, data[i], data[i + 1], last);
            }

            if (last != null && Equals(last, last.next))
            {
                RemoveNode(last);
                last = last.next;
            }

            return last;
        }

        // eliminate colinear or duplicate points
        static Node FilterPoints(Node start, Node end)
        {
            if (start == null) return start;
            if (end == null) end = start;

            Node p = start;
            bool again;
            do
            {
                again = false;

                if (!p.steiner && (Equals(p, p.next) || Area(p.prev, p, p.next) == 0))
                {
                    RemoveNode(p);
                    p = end = p.prev;
                    if (p == p.next) break;
                    again = true;
                }
                else
                {
                    p = p.next;
                }
            } while (again || p != end);

            return end;
        }

        // main ear slicing loop which triangulates a polygon (given as a linked list)
        static void EarcutLinked(Node ear, List<int> triangles, int dim, double minX, double minY, double invSize, int pass)
        {
            if (ear == null) return;

            // interlink polygon nodes in z-order
            if (pass == 0 && invSize != 0) IndexCurve(ear, minX, minY, invSize);

            var stop = ear;

            // iterate through ears, slicing them one by one
            while (ear.prev != ear.next)
            {
                var prev = ear.prev;
                var next = ear.next;

                if (invSize != 0 ? IsEarHashed(ear, minX, minY, invSize) : IsEar(ear))
                {
                    triangles.Add(prev.i); triangles.Add(ear.i); triangles.Add(next.i); // cut off the triangle

                    RemoveNode(ear);

                    // skipping the next vertex leads to less sliver triangles
                    ear = next.next;
                    stop = next.next;

                    continue;
                }

                ear = next;

                // if we looped through the whole remaining polygon and can't find any more ears
                if (ear == stop)
                {
                    // try filtering points and slicing again
                    if (pass == 0)
                    {
                        EarcutLinked(FilterPoints(ear, null), triangles, dim, minX, minY, invSize, 1);
                    // if this didn't work, try curing all small self-intersections locally
                    }
                    else if (pass == 1)
                    {
                        ear = CureLocalIntersections(FilterPoints(ear, null), triangles);
                        EarcutLinked(ear, triangles, dim, minX, minY, invSize, 2);
                    // as a last resort, try splitting the remaining polygon into two
                    }
                    else if (pass == 2)
                    {
                        SplitEarcut(ear, triangles, dim, minX, minY, invSize);
                    }

                    break;
                }
            }
        }

        // check whether a polygon node forms a valid ear with adjacent nodes
        static bool IsEar(Node ear)
        {
            var a = ear.prev;
            var b = ear;
            var c = ear.next;

            if (Area(a, b, c) >= 0) return false; // reflex, can't be an ear

            // now make sure we don't have other points inside the potential ear
            double ax = a.x, bx = b.x, cx = c.x, ay = a.y, by = b.y, cy = c.y;

            // triangle bbox
            double x0 = Math.Min(ax, Math.Min(bx, cx)),
                y0 = Math.Min(ay, Math.Min(by, cy)),
                x1 = Math.Max(ax, Math.Max(bx, cx)),
                y1 = Math.Max(ay, Math.Max(by, cy));

            var p = c.next;
            while (p != a)
            {
                if (p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1 &&
                    PointInTriangleExceptFirst(ax, ay, bx, by, cx, cy, p.x, p.y) &&
                    Area(p.prev, p, p.next) >= 0) return false;
                p = p.next;
            }

            return true;
        }

        static bool IsEarHashed(Node ear, double minX, double minY, double invSize)
        {
            var a = ear.prev;
            var b = ear;
            var c = ear.next;

            if (Area(a, b, c) >= 0) return false; // reflex, can't be an ear

            double ax = a.x, bx = b.x, cx = c.x, ay = a.y, by = b.y, cy = c.y;

            // triangle bbox
            double x0 = Math.Min(ax, Math.Min(bx, cx)),
                y0 = Math.Min(ay, Math.Min(by, cy)),
                x1 = Math.Max(ax, Math.Max(bx, cx)),
                y1 = Math.Max(ay, Math.Max(by, cy));

            // z-order range for the current triangle bbox;
            double minZ = ZOrder(x0, y0, minX, minY, invSize),
                maxZ = ZOrder(x1, y1, minX, minY, invSize);

            var p = ear.prevZ;
            var n = ear.nextZ;

            // look for points inside the triangle in both directions
            while (p != null && p.z >= minZ && n != null && n.z <= maxZ)
            {
                if (p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1 && p != a && p != c &&
                    PointInTriangleExceptFirst(ax, ay, bx, by, cx, cy, p.x, p.y) && Area(p.prev, p, p.next) >= 0) return false;
                p = p.prevZ;

                if (n.x >= x0 && n.x <= x1 && n.y >= y0 && n.y <= y1 && n != a && n != c &&
                    PointInTriangleExceptFirst(ax, ay, bx, by, cx, cy, n.x, n.y) && Area(n.prev, n, n.next) >= 0) return false;
                n = n.nextZ;
            }

            // look for remaining points in decreasing z-order
            while (p != null && p.z >= minZ)
            {
                if (p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1 && p != a && p != c &&
                    PointInTriangleExceptFirst(ax, ay, bx, by, cx, cy, p.x, p.y) && Area(p.prev, p, p.next) >= 0) return false;
                p = p.prevZ;
            }

            // look for remaining points in increasing z-order
            while (n != null && n.z <= maxZ)
            {
                if (n.x >= x0 && n.x <= x1 && n.y >= y0 && n.y <= y1 && n != a && n != c &&
                    PointInTriangleExceptFirst(ax, ay, bx, by, cx, cy, n.x, n.y) && Area(n.prev, n, n.next) >= 0) return false;
                n = n.nextZ;
            }

            return true;
        }

        // go through all polygon nodes and cure small local self-intersections
        static Node CureLocalIntersections(Node start, List<int> triangles)
        {
            Node p = start;
            do
            {
                var a = p.prev;
                var b = p.next.next;

                if (!Equals(a, b) && Intersects(a, p, p.next, b) && LocallyInside(a, b) && LocallyInside(b, a))
                {
                    triangles.Add(a.i); triangles.Add(p.i); triangles.Add(b.i);

                    // remove two nodes involved
                    RemoveNode(p);
                    RemoveNode(p.next);

                    p = start = b;
                }
                p = p.next;
            } while (p != start);

            return FilterPoints(p, null);
        }

        // try splitting polygon into two and triangulate them independently
        static void SplitEarcut(Node start, List<int> triangles, int dim, double minX, double minY, double invSize)
        {
            // look for a valid diagonal that divides the polygon into two
            Node a = start;
            do
            {
                Node b = a.next.next;
                while (b != a.prev)
                {
                    if (a.i != b.i && IsValidDiagonal(a, b))
                    {
                        // split the polygon in two by the diagonal
                        Node c = SplitPolygon(a, b);

                        // filter colinear points around the cuts
                        a = FilterPoints(a, a.next);
                        c = FilterPoints(c, c.next);

                        // run earcut on each half
                        EarcutLinked(a, triangles, dim, minX, minY, invSize, 0);
                        EarcutLinked(c, triangles, dim, minX, minY, invSize, 0);
                        return;
                    }
                    b = b.next;
                }
                a = a.next;
            } while (a != start);
        }

        // link every hole into the outer loop, producing a single-ring polygon without holes
        static Node EliminateHoles(double[] data, int[] holeIndices, Node outerNode, int dim)
        {
            var queue = new List<Node>();

            for (int i = 0, len = holeIndices.Length; i < len; i++)
            {
                int start = holeIndices[i] * dim;
                int end = i < len - 1 ? holeIndices[i + 1] * dim : data.Length;
                var list = LinkedList(data, start, end, dim, false);
                if (list == list.next) list.steiner = true;
                queue.Add(GetLeftmost(list));
            }

            queue.Sort(CompareXYSlope);

            // process holes from left to right
            for (int i = 0; i < queue.Count; i++)
            {
                outerNode = EliminateHole(queue[i], outerNode);
            }

            return outerNode;
        }

        static int CompareXYSlope(Node a, Node b)
        {
            double result = a.x - b.x;
            // when the left-most point of 2 holes meet at a vertex, sort the holes counterclockwise so that when we find
            // the bridge to the outer shell is always the point that they meet at.
            if (result == 0)
            {
                result = a.y - b.y;
                if (result == 0)
                {
                    double aSlope = (a.next.y - a.y) / (a.next.x - a.x);
                    double bSlope = (b.next.y - b.y) / (b.next.x - b.x);
                    result = aSlope - bSlope;
                }
            }
            return result < 0 ? -1 : result > 0 ? 1 : 0;
        }

        // find a bridge between vertices that connects hole with an outer ring and link it
        static Node EliminateHole(Node hole, Node outerNode)
        {
            var bridge = FindHoleBridge(hole, outerNode);
            if (bridge == null) return outerNode;

            var bridgeReverse = SplitPolygon(bridge, hole);

            // filter collinear points around the cuts
            FilterPoints(bridgeReverse, bridgeReverse.next);
            return FilterPoints(bridge, bridge.next);
        }

        // David Eberly's algorithm for finding a bridge between hole and outer polygon
        static Node FindHoleBridge(Node hole, Node outerNode)
        {
            Node p = outerNode;
            double hx = hole.x;
            double hy = hole.y;
            double qx = double.NegativeInfinity;
            Node m = null;

            // find a segment intersected by a ray from the hole's leftmost point to the left;
            // segment's endpoint with lesser x will be potential connection point
            // unless they intersect at a vertex, then choose the vertex
            if (Equals(hole, p)) return p;
            do
            {
                if (Equals(hole, p.next)) return p.next;
                if (hy <= p.y && hy >= p.next.y && p.next.y != p.y)
                {
                    double x = p.x + (hy - p.y) * (p.next.x - p.x) / (p.next.y - p.y);
                    if (x <= hx && x > qx)
                    {
                        qx = x;
                        m = p.x < p.next.x ? p : p.next;
                        if (x == hx) return m; // hole touches outer segment; pick leftmost endpoint
                    }
                }
                p = p.next;
            } while (p != outerNode);

            if (m == null) return null;

            // look for points inside the triangle of hole point, segment intersection and endpoint;
            // if there are no points found, we have a valid connection;
            // otherwise choose the point of the minimum angle with the ray as connection point

            var stop = m;
            double mx = m.x;
            double my = m.y;
            double tanMin = double.PositiveInfinity;

            p = m;

            do
            {
                if (hx >= p.x && p.x >= mx && hx != p.x &&
                    PointInTriangle(hy < my ? hx : qx, hy, mx, my, hy < my ? qx : hx, hy, p.x, p.y))
                {
                    double tan = Math.Abs(hy - p.y) / (hx - p.x); // tangential

                    if (LocallyInside(p, hole) &&
                        (tan < tanMin || (tan == tanMin && (p.x > m.x || (p.x == m.x && SectorContainsSector(m, p))))))
                    {
                        m = p;
                        tanMin = tan;
                    }
                }

                p = p.next;
            } while (p != stop);

            return m;
        }

        // whether sector in vertex m contains sector in vertex p in the same coordinates
        static bool SectorContainsSector(Node m, Node p)
        {
            return Area(m.prev, m, p.prev) < 0 && Area(p.next, m, m.next) < 0;
        }

        // interlink polygon nodes in z-order
        static void IndexCurve(Node start, double minX, double minY, double invSize)
        {
            Node p = start;
            do
            {
                if (p.z == 0) p.z = ZOrder(p.x, p.y, minX, minY, invSize);
                p.prevZ = p.prev;
                p.nextZ = p.next;
                p = p.next;
            } while (p != start);

            p.prevZ.nextZ = null;
            p.prevZ = null;

            SortLinked(p);
        }

        // Simon Tatham's linked list merge sort algorithm
        // http://www.chiark.greenend.org.uk/~sgtatham/algorithms/listsort.html
        static Node SortLinked(Node list)
        {
            int numMerges;
            int inSize = 1;

            do
            {
                Node p = list;
                Node e;
                list = null;
                Node tail = null;
                numMerges = 0;

                while (p != null)
                {
                    numMerges++;
                    Node q = p;
                    int pSize = 0;
                    for (int i = 0; i < inSize; i++)
                    {
                        pSize++;
                        q = q.nextZ;
                        if (q == null) break;
                    }
                    int qSize = inSize;

                    while (pSize > 0 || (qSize > 0 && q != null))
                    {
                        if (pSize != 0 && (qSize == 0 || q == null || p.z <= q.z))
                        {
                            e = p;
                            p = p.nextZ;
                            pSize--;
                        }
                        else
                        {
                            e = q;
                            q = q.nextZ;
                            qSize--;
                        }

                        if (tail != null) tail.nextZ = e;
                        else list = e;

                        e.prevZ = tail;
                        tail = e;
                    }

                    p = q;
                }

                tail.nextZ = null;
                inSize *= 2;
            } while (numMerges > 1);

            return list;
        }

        // z-order of a point given coords and inverse of the longer side of data bbox
        static double ZOrder(double x, double y, double minX, double minY, double invSize)
        {
            // coords are transformed into non-negative 15-bit integer range
            int xi = (int)((x - minX) * invSize); // JS |0
            int yi = (int)((y - minY) * invSize);

            xi = (xi | (xi << 8)) & 0x00FF00FF;
            xi = (xi | (xi << 4)) & 0x0F0F0F0F;
            xi = (xi | (xi << 2)) & 0x33333333;
            xi = (xi | (xi << 1)) & 0x55555555;

            yi = (yi | (yi << 8)) & 0x00FF00FF;
            yi = (yi | (yi << 4)) & 0x0F0F0F0F;
            yi = (yi | (yi << 2)) & 0x33333333;
            yi = (yi | (yi << 1)) & 0x55555555;

            return xi | (yi << 1);
        }

        // find the leftmost node of a polygon ring
        static Node GetLeftmost(Node start)
        {
            Node p = start, leftmost = start;
            do
            {
                if (p.x < leftmost.x || (p.x == leftmost.x && p.y < leftmost.y)) leftmost = p;
                p = p.next;
            } while (p != start);

            return leftmost;
        }

        // check if a point lies within a convex triangle
        static bool PointInTriangle(double ax, double ay, double bx, double by, double cx, double cy, double px, double py)
        {
            return (cx - px) * (ay - py) >= (ax - px) * (cy - py) &&
                   (ax - px) * (by - py) >= (bx - px) * (ay - py) &&
                   (bx - px) * (cy - py) >= (cx - px) * (by - py);
        }

        // check if a point lies within a convex triangle but false if its equal to the first point of the triangle
        static bool PointInTriangleExceptFirst(double ax, double ay, double bx, double by, double cx, double cy, double px, double py)
        {
            return !(ax == px && ay == py) && PointInTriangle(ax, ay, bx, by, cx, cy, px, py);
        }

        // check if a diagonal between two polygon nodes is valid (lies in polygon interior)
        static bool IsValidDiagonal(Node a, Node b)
        {
            return a.next.i != b.i && a.prev.i != b.i && !IntersectsPolygon(a, b) && // doesn't intersect other edges
                   (LocallyInside(a, b) && LocallyInside(b, a) && MiddleInside(a, b) && // locally visible
                    (Area(a.prev, a, b.prev) != 0 || Area(a, b.prev, b) != 0) || // does not create opposite-facing sectors
                    Equals(a, b) && Area(a.prev, a, a.next) > 0 && Area(b.prev, b, b.next) > 0); // special zero-length case
        }

        // signed area of a triangle
        static double Area(Node p, Node q, Node r)
        {
            return (q.y - p.y) * (r.x - q.x) - (q.x - p.x) * (r.y - q.y);
        }

        // check if two points are equal
        static bool Equals(Node p1, Node p2)
        {
            return p1.x == p2.x && p1.y == p2.y;
        }

        // check if two segments intersect
        static bool Intersects(Node p1, Node q1, Node p2, Node q2)
        {
            int o1 = Sign(Area(p1, q1, p2));
            int o2 = Sign(Area(p1, q1, q2));
            int o3 = Sign(Area(p2, q2, p1));
            int o4 = Sign(Area(p2, q2, q1));

            if (o1 != o2 && o3 != o4) return true; // general case

            if (o1 == 0 && OnSegment(p1, p2, q1)) return true; // p1, q1 and p2 are collinear and p2 lies on p1q1
            if (o2 == 0 && OnSegment(p1, q2, q1)) return true; // p1, q1 and q2 are collinear and q2 lies on p1q1
            if (o3 == 0 && OnSegment(p2, p1, q2)) return true; // p2, q2 and p1 are collinear and p1 lies on p2q2
            if (o4 == 0 && OnSegment(p2, q1, q2)) return true; // p2, q2 and q1 are collinear and q1 lies on p2q2

            return false;
        }

        // for collinear points p, q, r, check if point q lies on segment pr
        static bool OnSegment(Node p, Node q, Node r)
        {
            return q.x <= Math.Max(p.x, r.x) && q.x >= Math.Min(p.x, r.x) && q.y <= Math.Max(p.y, r.y) && q.y >= Math.Min(p.y, r.y);
        }

        static int Sign(double num)
        {
            return num > 0 ? 1 : num < 0 ? -1 : 0;
        }

        // check if a polygon diagonal intersects any polygon segments
        static bool IntersectsPolygon(Node a, Node b)
        {
            Node p = a;
            do
            {
                if (p.i != a.i && p.next.i != a.i && p.i != b.i && p.next.i != b.i &&
                    Intersects(p, p.next, a, b)) return true;
                p = p.next;
            } while (p != a);

            return false;
        }

        // check if a polygon diagonal is locally inside the polygon
        static bool LocallyInside(Node a, Node b)
        {
            return Area(a.prev, a, a.next) < 0
                ? Area(a, b, a.next) >= 0 && Area(a, a.prev, b) >= 0
                : Area(a, b, a.prev) < 0 || Area(a, a.next, b) < 0;
        }

        // check if the middle point of a polygon diagonal is inside the polygon
        static bool MiddleInside(Node a, Node b)
        {
            Node p = a;
            bool inside = false;
            double px = (a.x + b.x) / 2;
            double py = (a.y + b.y) / 2;
            do
            {
                if (((p.y > py) != (p.next.y > py)) && p.next.y != p.y &&
                    (px < (p.next.x - p.x) * (py - p.y) / (p.next.y - p.y) + p.x))
                    inside = !inside;
                p = p.next;
            } while (p != a);

            return inside;
        }

        // link two polygon vertices with a bridge; if the vertices belong to the same ring, it splits polygon into two;
        // if one belongs to the outer ring and another to a hole, it merges it into a single ring
        static Node SplitPolygon(Node a, Node b)
        {
            var a2 = CreateNode(a.i, a.x, a.y);
            var b2 = CreateNode(b.i, b.x, b.y);
            var an = a.next;
            var bp = b.prev;

            a.next = b;
            b.prev = a;

            a2.next = an;
            an.prev = a2;

            b2.next = a2;
            a2.prev = b2;

            bp.next = b2;
            b2.prev = bp;

            return b2;
        }

        // create a node and optionally link it with previous one (in a circular doubly linked list)
        static Node InsertNode(int i, double x, double y, Node last)
        {
            var p = CreateNode(i, x, y);

            if (last == null)
            {
                p.prev = p;
                p.next = p;
            }
            else
            {
                p.next = last.next;
                p.prev = last;
                last.next.prev = p;
                last.next = p;
            }
            return p;
        }

        static void RemoveNode(Node p)
        {
            p.next.prev = p.prev;
            p.prev.next = p.next;

            if (p.prevZ != null) p.prevZ.nextZ = p.nextZ;
            if (p.nextZ != null) p.nextZ.prevZ = p.prevZ;
        }

        static Node CreateNode(int i, double x, double y)
        {
            return new Node { i = i, x = x, y = y };
        }

        static double SignedArea(double[] data, int start, int end, int dim)
        {
            double sum = 0;
            for (int i = start, j = end - dim; i < end; i += dim)
            {
                sum += (data[j] - data[i]) * (data[i + 1] + data[j + 1]);
                j = i;
            }
            return sum;
        }
    }
}
