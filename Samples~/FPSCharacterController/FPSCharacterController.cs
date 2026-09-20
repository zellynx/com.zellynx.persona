using UnityEngine;
using Character.Mobility;

namespace CharacterMovementFramework
{
    /// <summary>
    /// Minimal movement-only FPS character controller.
    ///
    /// Camera and look are intentionally outside this sample.
    /// CharacterBody owns physical movement, gravity, grounding, slopes,
    /// collisions, steps, ledges and platform inheritance.
    /// </summary>
    [RequireComponent(typeof(CharacterBody))]
    public sealed class FPSCharacterController : MonoBehaviour
    {
        [SerializeField]
        private float _moveSpeed = 6f;

        [SerializeField]
        private float _jumpSpeed = 8f;

        private CharacterBody _body;

        private void Awake()
        {
            _body = GetComponent<CharacterBody>();
        }

        private void FixedUpdate()
        {
            Vector3 input =
                new Vector3(
                    Input.GetAxisRaw("Horizontal"),
                    0f,
                    Input.GetAxisRaw("Vertical")
                );

            input = Vector3.ClampMagnitude(input, 1f);

            Vector3 movementDirection =
                _body.Basis.Right * input.x +
                _body.Basis.Forward * input.z;

            if (movementDirection.sqrMagnitude > 1f)
            {
                movementDirection.Normalize();
            }

            Vector3 requestedVelocity =
                movementDirection * _moveSpeed;

            if (_body.IsGrounded &&
                Input.GetButton("Jump"))
            {
                requestedVelocity +=
                    _body.Basis.Up * _jumpSpeed;

                _body.ForceUnground(0.1f);
            }

            _body.Move(
                new VelocityUpdateRequest
                {
                    Velocity = new Velocity {
                        Linear = requestedVelocity,
                        Angular = Vector3.zero
                    },

                    DeltaTime = Time.fixedDeltaTime
                }
            );
        }
    }
}