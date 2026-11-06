// @role Provide a developer-only free-flight camera and collision inspection in exported physics sandbox scenes.
// @calls VoxelBodyHitbox, VoxelAuthoring, VoxelStructureRuntime
// @tune flySpeed:float:1-500:Sandbox camera speed in metres per second.
// @tune lookSensitivity:float:0.01-1:Degrees of camera rotation per mouse pixel.
// @tune pushSpeed:float:0.1-20:Velocity impulse applied by the sandbox's middle-click collision probe.

using UnityEngine;
using UnityEngine.InputSystem;

namespace Starframe.Maps
{
    [RequireComponent(typeof(Camera))]
    public sealed class MapSandboxCamera : MonoBehaviour
    {
        [Min(1)] public float flySpeed = 60;
        [Range(.01f, 1)] public float lookSensitivity = .12f;
        [Range(.1f, 20)] public float pushSpeed = 3;
        Camera view;
        string lastHit = "Aim at scenery and click to inspect a cell. Middle click applies an impulse.";
        void Awake() => view = GetComponent<Camera>();
        void Update()
        {
            var mouse = Mouse.current; var keys = Keyboard.current; if (mouse == null || keys == null) return;
            bool look = mouse.rightButton.isPressed;
            Cursor.lockState = look ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !look;
            if (look)
            {
                Vector2 delta = mouse.delta.ReadValue() * lookSensitivity;
                transform.rotation = Quaternion.AngleAxis(delta.x, Vector3.up) * transform.rotation * Quaternion.AngleAxis(-delta.y, Vector3.right);
            }
            Vector3 move = new Vector3((keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0),
                (keys.spaceKey.isPressed ? 1 : 0) - (keys.leftCtrlKey.isPressed ? 1 : 0), (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0));
            transform.position += transform.TransformDirection(Vector3.ClampMagnitude(move, 1)) * flySpeed * (keys.leftShiftKey.isPressed ? 3 : 1) * Time.deltaTime;
            flySpeed = Mathf.Clamp(flySpeed * Mathf.Exp(mouse.scroll.ReadValue().y * .001f), 1, 500);
            if (mouse.middleButton.wasPressedThisFrame)
            {
                var ray = view.ViewportPointToRay(new Vector3(.5f, .5f));
                if (Physics.Raycast(ray, out var hit, 10000, ~0, QueryTriggerInteraction.Ignore) && hit.rigidbody && hit.collider.GetComponent<VoxelBodyHitbox>())
                { hit.rigidbody.AddForceAtPosition(ray.direction * hit.rigidbody.mass * pushSpeed, hit.point, ForceMode.Impulse); lastHit = "Pushed " + hit.rigidbody.name; }
            }
            if (mouse.leftButton.wasPressedThisFrame)
            {
                Ray ray = view.ViewportPointToRay(new Vector3(.5f, .5f));
                if (Physics.Raycast(ray, out var hit, 10000, ~0, QueryTriggerInteraction.Ignore))
                {
                    var target = hit.collider.GetComponent<VoxelBodyHitbox>();
                    if (target && VoxelAuthoring.Pick(target.Structure, ray, out var cell, out _, out _))
                    {
                        lastHit = target.name + " | Cell " + cell;
                    }
                }
            }
        }
        // This overlay belongs to the development sandbox; it is not part of the player's game HUD.
        void OnGUI()
        {
            GUI.Label(new Rect(18, 14, Screen.width - 36, 24), "MAP PHYSICS SANDBOX | RMB look | WASD move | Space/Ctrl up/down | Wheel speed | LMB inspect | MMB push | Shift boost");
            GUI.Label(new Rect(18, 39, Screen.width - 36, 24), lastHit);
            GUI.Label(new Rect(Screen.width * .5f - 5, Screen.height * .5f - 10, 20, 20), "+");
        }
        void OnDisable() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }
}
