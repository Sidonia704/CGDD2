// @role Integrate legacy occupied cubes into preserved mass, centre and principal inertia before sparse resampling.
// @calls VoxelAssetDefinition

using System;
using Starframe.Maps;
using UnityEngine;

namespace Starframe.EditorTools
{
    public static class UniformPhysicsCapture
    {
        // CollisionBoxes is an exact disjoint union of these full occupied cubes.
        // Integrate that volume directly; an unstepped temporary Rigidbody returns default mass properties.
        public static VoxelPhysicalMetadata Capture(VoxelAssetDefinition source, float scale)
        {
            if (!source) throw new ArgumentException("Missing source.");
            if (!source.Validate(out var error)) throw new ArgumentException(error);
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
            int count = source.cells.Count;
            if (count == 0) throw new ArgumentException("Cannot capture an empty source.");
            // Keep the legacy float expression and mass clamp, even when it clamps a very large object.
            float mass = Mathf.Clamp(count * source.massPerCell * Mathf.Pow(source.cellSize, 3) *
                Mathf.Abs(scale * scale * scale), .01f, 100000000);
            double x = 0, y = 0, z = 0;
            foreach (var cell in source.cells) { x += cell.position.x; y += cell.position.y; z += cell.position.z; }
            x /= count; y /= count; z /= count;
            double size = (double)source.cellSize * scale;
            double intrinsic = size * size / 6.0;
            double weight = (double)mass / count;
            var tensor = new double[3, 3];
            foreach (var cell in source.cells)
            {
                double rx = (cell.position.x - x) * size;
                double ry = (cell.position.y - y) * size;
                double rz = (cell.position.z - z) * size;
                tensor[0, 0] += weight * (ry * ry + rz * rz + intrinsic);
                tensor[1, 1] += weight * (rx * rx + rz * rz + intrinsic);
                tensor[2, 2] += weight * (rx * rx + ry * ry + intrinsic);
                tensor[0, 1] -= weight * rx * ry;
                tensor[0, 2] -= weight * rx * rz;
                tensor[1, 2] -= weight * ry * rz;
            }
            tensor[1, 0] = tensor[0, 1]; tensor[2, 0] = tensor[0, 2]; tensor[2, 1] = tensor[1, 2];
            PrincipalAxes(tensor, out var principal, out var rotation);
            var result = new VoxelPhysicalMetadata { valid = true, mass = mass,
                centerOfMass = new Vector3((float)(x * size), (float)(y * size), (float)(z * size)),
                inertiaTensor = principal, inertiaTensorRotation = rotation };
            if (!result.IsValid) throw new InvalidOperationException("Integrated legacy mass properties are invalid.");
            return result;
        }

        static void PrincipalAxes(double[,] a, out Vector3 diagonal, out Quaternion rotation)
        {
            var v = new double[3, 3]; for (int i = 0; i < 3; i++) v[i, i] = 1;
            // Jacobi similarity rotations retain eigenvectors as columns, in the source's local frame.
            for (int iteration = 0; iteration < 48; iteration++)
            {
                int p = 0, q = 1;
                double off = Math.Abs(a[p, q]);
                if (Math.Abs(a[0, 2]) > off) { p = 0; q = 2; off = Math.Abs(a[0, 2]); }
                if (Math.Abs(a[1, 2]) > off) { p = 1; q = 2; off = Math.Abs(a[1, 2]); }
                double magnitude = Math.Max(Math.Abs(a[0, 0]), Math.Max(Math.Abs(a[1, 1]), Math.Abs(a[2, 2])));
                if (off <= Math.Max(1e-20, magnitude * 1e-14)) break;
                double angle = .5 * Math.Atan2(2 * a[p, q], a[q, q] - a[p, p]);
                double c = Math.Cos(angle), s = Math.Sin(angle);
                double app = a[p, p], aqq = a[q, q], apq = a[p, q];
                a[p, p] = c * c * app - 2 * c * s * apq + s * s * aqq;
                a[q, q] = s * s * app + 2 * c * s * apq + c * c * aqq;
                a[p, q] = a[q, p] = 0;
                for (int k = 0; k < 3; k++)
                {
                    if (k != p && k != q)
                    {
                        double akp = a[k, p], akq = a[k, q];
                        a[k, p] = a[p, k] = c * akp - s * akq;
                        a[k, q] = a[q, k] = s * akp + c * akq;
                    }
                    double vkp = v[k, p], vkq = v[k, q];
                    v[k, p] = c * vkp - s * vkq; v[k, q] = s * vkp + c * vkq;
                }
            }
            diagonal = new Vector3((float)Math.Max(1e-8, a[0, 0]),
                (float)Math.Max(1e-8, a[1, 1]), (float)Math.Max(1e-8, a[2, 2]));
            var right = new Vector3((float)v[0, 0], (float)v[1, 0], (float)v[2, 0]).normalized;
            var up = new Vector3((float)v[0, 1], (float)v[1, 1], (float)v[2, 1]);
            up = (up - right * Vector3.Dot(right, up)).normalized;
            var forward = Vector3.Cross(right, up).normalized;
            up = Vector3.Cross(forward, right).normalized;
            rotation = Quaternion.LookRotation(forward, up).normalized;
        }
    }
}
