using System;
using UnityEngine;

namespace Character.Mobility
{
    /// <summary>
    /// The semantic coordinate frame of a CharacterBody.
    ///
    /// Up is the character's semantic vertical/traversal direction.
    /// Forward is the character's semantic facing direction.
    /// Right is derived from Up and Forward.
    ///
    /// This is not simply a wrapper around Transform.
    /// The initial basis may be created from a Transform, but afterward it is
    /// semantic CharacterBody state.
    /// </summary>
    public readonly struct Orientation
    {
        private const float DegenerateEpsilon = 0.0001f;

        /// <summary>
        /// Character's semantic vertical / traversal direction.
        /// </summary>
        public Vector3 Up { get; }

        /// <summary>
        /// Character's semantic facing direction.
        /// </summary>
        public Vector3 Forward { get; }

        /// <summary>
        /// Character's derived lateral direction.
        /// </summary>
        public Vector3 Right => Vector3.Cross(Up, Forward);
        
        public bool IsValid
        {
            get
            {
                if (Up.sqrMagnitude < DegenerateEpsilon ||
                    Forward.sqrMagnitude < DegenerateEpsilon)
                {
                    return false;
                }

                const float tolerance = 0.001f;

                return
                    Mathf.Abs(Up.sqrMagnitude - 1f) <= tolerance &&
                    Mathf.Abs(Forward.sqrMagnitude - 1f) <= tolerance &&
                    Mathf.Abs(Vector3.Dot(Up, Forward)) <= tolerance;
            }
        }

        private Orientation(Vector3 up, Vector3 forward) {
            Up = up;
            Forward = forward;
        }
        
        public static Orientation FromTransform(
            Transform transform)
        {
            if (transform == null)
            {
                throw new ArgumentNullException(nameof(transform));
            }

            Vector3 up =
                transform.up.normalized;

            Vector3 forward =
                ProjectOntoPlane(
                    transform.forward,
                    up
                );

            if (forward.sqrMagnitude < DegenerateEpsilon)
            {
                forward =
                    ProjectOntoPlane(
                        transform.right,
                        up
                    );
            }

            if (forward.sqrMagnitude < DegenerateEpsilon)
            {
                GetFallbackForward(
                    up,
                    out forward
                );
            }
            else
            {
                forward.Normalize();
            }

            return new Orientation(
                up,
                forward
            );
        }

        /// <summary>
        /// Returns a new basis with a different semantic Up direction while
        /// preserving the current facing direction as much as possible.
        /// </summary>
        public Orientation WithUpDirection(Vector3 newUp) {
            if (newUp.sqrMagnitude < DegenerateEpsilon) {
                return this;
            }

            newUp.Normalize();

            Vector3 newForward = ProjectOntoPlane(Forward, newUp);

            if (newForward.sqrMagnitude < DegenerateEpsilon) {
                newForward = ProjectOntoPlane(Right, newUp);
            }

            if (newForward.sqrMagnitude < DegenerateEpsilon) {
                GetFallbackForward(newUp, out newForward);
            }
            else {
                newForward.Normalize();
            }

            return new Orientation(
                newUp,
                newForward
            );
        }

        /// <summary>
        /// Returns a new basis with a different semantic facing direction while
        /// preserving the current Up direction.
        /// </summary>
        public Orientation WithForwardDirection(Vector3 newForward) {
            Vector3 projected = ProjectOntoPlane(newForward, Up);

            if (projected.sqrMagnitude < DegenerateEpsilon) {
                return this;
            }

            projected.Normalize();

            return new Orientation(
                Up,
                projected
            );
        }

        private static Vector3 ProjectOntoPlane(
            Vector3 direction,
            Vector3 planeNormal) {
            return direction - planeNormal * Vector3.Dot(direction, planeNormal);
        }

        private static void GetFallbackForward(
            Vector3 up,
            out Vector3 forward) {
            Vector3 reference =
                Mathf.Abs(Vector3.Dot(up, Vector3.forward)) < 0.9f
                    ? Vector3.forward
                    : Vector3.right;

            forward = ProjectOntoPlane(reference, up);

            if (forward.sqrMagnitude < DegenerateEpsilon) {
                reference = Vector3.up;
                forward = ProjectOntoPlane(reference, up);
            }

            forward.Normalize();
        }
    }
}