// @role Project offscreen targets onto the HUD boundary.

using UnityEngine;

namespace Starframe.Combat
{
    public static class TargetIndicatorProjection
    {
        public static bool TryEdge(Camera view, Vector3 worldPosition, Rect rect, float inset,
            out Vector2 edge, out Vector2 direction)
        {
            edge = default;
            direction = default;
            if (!view) return false;

            Vector3 local = view.transform.InverseTransformPoint(worldPosition);
            if (local.z > 0f)
            {
                Vector3 viewport = view.WorldToViewportPoint(worldPosition);
                if (viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f &&
                    viewport.y >= 0f && viewport.y <= 1f)
                    return false;
            }

            // Use screen-plane orientation, while preserving X/Y signs behind the camera.
            // The common perspective and depth factors cancel when finding a rectangle edge.
            float safeAspect = Mathf.Max(.0001f, view.aspect);
            Vector2 screenDirection = new Vector2(local.x * rect.width / safeAspect, local.y * rect.height);
            if (screenDirection.sqrMagnitude < .000001f)
                screenDirection = Vector2.down;
            direction = screenDirection.normalized;

            float halfWidth = Mathf.Max(0f, rect.width * .5f - Mathf.Max(0f, inset));
            float halfHeight = Mathf.Max(0f, rect.height * .5f - Mathf.Max(0f, inset));
            float tx = Mathf.Abs(direction.x) > .000001f ? halfWidth / Mathf.Abs(direction.x) : float.PositiveInfinity;
            float ty = Mathf.Abs(direction.y) > .000001f ? halfHeight / Mathf.Abs(direction.y) : float.PositiveInfinity;
            float distance = Mathf.Min(tx, ty);
            edge = rect.center + direction * distance;
            return true;
        }
    }
}
