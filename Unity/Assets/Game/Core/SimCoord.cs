using UnityEngine;

namespace NeonKaido
{
    /// Coordinate bridge between the JS sim space and Unity.
    /// sim (x, y, z) == Unity (x, y, -z).  Y-up in both.
    /// Sim yaw 0 = north = sim -Z = Unity +Z. Sim forward = (-sin yaw, -cos yaw) in (x, z).
    /// Convention: all generated world data is computed in SIM space (formulas stay
    /// line-by-line identical to the JS) and converted here at the mesh/transform boundary.
    public static class SimCoord
    {
        public static Vector3 ToUnity(double x, double y, double z) => new Vector3((float)x, (float)y, (float)-z);

        public static Vector3 ToUnity(float x, float y, float z) => new Vector3(x, y, -z);

        public static Vector3 FromUnity(Vector3 u) => new Vector3(u.x, u.y, -u.z);

        /// Camera rotation for a sim-space yaw/pitch. Unity camera yaw = 180 - simYaw;
        /// Euler x > 0 looks down.
        public static Quaternion CamRot(double yawDeg, double pitchDeg) =>
            Quaternion.Euler((float)pitchDeg, (float)(180 - yawDeg), 0f);
    }
}
