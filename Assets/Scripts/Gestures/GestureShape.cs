using System.Collections.Generic;
using UnityEngine;

namespace GestureRecognition
{
    /// <summary>
    /// Base definition for gestures that can be evaluated by the <see cref="GestureDetector"/>.
    /// Extend this class to implement custom detection logic.
    /// </summary>
    public abstract class GestureShape : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Identifier used when reporting detections.")]
        private string shapeId = "Shape";

        [SerializeField]
        [Min(0f)]
        [Tooltip("Cooldown enforced by the detector between consecutive matches of this shape (seconds).")]
        private float detectionCooldown = 1f;

        [SerializeField]
        [Tooltip("Colour used for debug gizmos when the detector draws the last recognised shape.")]
        private Color gizmoColor = Color.green;

        [SerializeField]
        [Min(3)]
        [Tooltip("Minimum number of samples required before this shape attempts to evaluate a gesture.")]
        private int minimumSampleCount = 15;

        /// <summary>
        /// Human friendly identifier of the shape used for logging and comparisons.
        /// </summary>
        public string ShapeId => string.IsNullOrWhiteSpace(shapeId) ? name : shapeId;

        /// <summary>
        /// Cooldown applied between consecutive matches of this shape.
        /// </summary>
        public float DetectionCooldown => detectionCooldown;

        /// <summary>
        /// Minimum amount of samples required before this shape attempts a match.
        /// </summary>
        public int MinimumSampleCount => Mathf.Max(3, minimumSampleCount);

        /// <summary>
        /// Colour used when drawing debug gizmos for the last recognised shape.
        /// </summary>
        public Color GizmoColor => gizmoColor;

        /// <summary>
        /// Attempts to match the current trail against the gesture definition.
        /// </summary>
        public abstract bool TryMatch(GestureDetector detector, GestureDetector.TrackedObject tracked, List<GestureDetector.Sample> samples, out GestureDetector.GestureMatch match);

        /// <summary>
        /// Draws debug gizmos to represent the provided match.
        /// </summary>
        public virtual void DrawGizmos(GestureDetector.GestureMatch match)
        {
        }

        /// <summary>
        /// Helper method that extracts sample positions into a standalone array.
        /// </summary>
        protected static Vector3[] ExtractPositions(List<GestureDetector.Sample> samples)
        {
            return GestureDetector.CopyPositions(samples);
        }
    }
}
