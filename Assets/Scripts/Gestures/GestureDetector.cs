using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace GestureRecognition
{
    /// <summary>
    /// Tracks the movement history of configured objects and raises events when gestures are recognised.
    /// Gesture analysis is delegated to pluggable <see cref="GestureShape"/> definitions.

    /// </summary>
    public class GestureDetector : MonoBehaviour
    {
        [Serializable]
        public class TrackedObject
        {
            [Tooltip("Transform that will be analysed for gesture recognition.")]
            public Transform target;

            [NonSerialized]
            internal readonly List<Sample> samples = new List<Sample>();

            [NonSerialized]
            internal readonly Dictionary<GestureShape, float> lastDetectionTimes = new Dictionary<GestureShape, float>();

            [NonSerialized]
            internal readonly Dictionary<GestureShape, GestureMatch> lastMatches = new Dictionary<GestureShape, GestureMatch>();
        }

        internal struct Sample
        {
            public Vector3 position;
            public float time;
        }

        [Serializable]
        public struct GestureMatch
        {
            public GestureShape shape;
            public Transform target;
            public Vector3 center;
            public float radius;
            public Vector3 normal;
            public float coverageAngle;
            public float travelDistance;
            public Vector3 travelDirection;
            public Vector3 startPosition;
            public Vector3 endPosition;
            public float duration;
            public bool isClockwise;
            public Vector3[] sampledPositions;
        }

        [Serializable]
        public class GestureMatchEvent : UnityEvent<GestureMatch>
        {
        }

        [Serializable]
        public class ShapeEvent
        {
            public GestureShape shape;
            public UnityEvent<Transform> onDetected = new UnityEvent<Transform>();

        }

        [Header("Tracked Objects")]
        [SerializeField]
        private List<TrackedObject> trackedObjects = new List<TrackedObject>();

        [SerializeField]
        [Min(0.005f)]
        [Tooltip("Interval in seconds between position samples for each tracked object.")]
        private float sampleInterval = 0.05f;

        [SerializeField]
        [Tooltip("Minimum distance an object must move before a new sample is stored.")]
        private float minPointDistance = 0.005f;

        [SerializeField]
        [Tooltip("Maximum lifetime of stored samples in seconds.")]
        private float maxSampleAge = 3f;

        [SerializeField]
        [Tooltip("Minimum number of samples required before gesture analysis starts.")]
        private int minSampleCount = 10;

        [Header("Shapes")]
        [SerializeField]
        private List<GestureShape> shapes = new List<GestureShape>();

        [SerializeField]
        private List<ShapeEvent> shapeEvents = new List<ShapeEvent>();

        [SerializeField]
        private GestureMatchEvent onGestureMatched = new GestureMatchEvent();

        [Header("Debug")]

        [SerializeField]
        [Tooltip("If enabled, successful detections are logged to the Unity console.")]
        private bool logDetections = true;

        [SerializeField]
        [Tooltip("Draws the captured trail and last detected gestures in the scene view during play mode.")]

        private bool drawDebug = true;

        [SerializeField]
        private Color trailColor = Color.cyan;

        private float sampleTimer;

        /// <summary>
        /// Collection of shapes that will be evaluated for every tracked object.
        /// </summary>
        public IReadOnlyList<GestureShape> Shapes => shapes;

        /// <summary>
        /// Event raised whenever any configured shape produces a successful match.
        /// </summary>
        public GestureMatchEvent OnGestureMatched => onGestureMatched;


        private void Update()
        {
            sampleTimer += Time.deltaTime;
            if (sampleTimer < sampleInterval)
            {
                return;
            }

            sampleTimer = 0f;
            float currentTime = Time.time;

            for (int i = trackedObjects.Count - 1; i >= 0; i--)
            {
                TrackedObject tracked = trackedObjects[i];
                if (tracked?.target == null)
                {
                    continue;
                }

                SampleObject(tracked, currentTime);

                if (tracked.samples.Count < minSampleCount)
                {
                    continue;
                }

                TryMatchShapes(tracked, currentTime);
            }
        }

        private void TryMatchShapes(TrackedObject tracked, float currentTime)
        {
            List<Sample> samples = tracked.samples;

            for (int i = 0; i < shapes.Count; i++)
            {
                GestureShape shape = shapes[i];
                if (shape == null)
                {
                    continue;
                }

                int requiredSamples = Mathf.Max(minSampleCount, shape.MinimumSampleCount);
                if (samples.Count < requiredSamples)
                {
                    continue;
                }

                if (!tracked.lastDetectionTimes.TryGetValue(shape, out float lastTime))
                {
                    lastTime = float.NegativeInfinity;
                }

                if (currentTime - lastTime < shape.DetectionCooldown)
                {
                    continue;
                }

                if (!shape.TryMatch(this, tracked, samples, out GestureMatch match))
                {
                    continue;
                }

                match.shape = shape;
                match.target = tracked.target;
                match.sampledPositions = match.sampledPositions ?? CopyPositions(samples);
                if (samples.Count > 0)
                {
                    match.startPosition = samples[0].position;
                    match.endPosition = samples[samples.Count - 1].position;
                    float duration = samples[samples.Count - 1].time - samples[0].time;
                    match.duration = duration > 0f ? duration : match.duration;
                }

                if (match.travelDistance <= 0f)
                {
                    match.travelDistance = CalculateTravelDistance(samples);
                }

                if (match.travelDirection == Vector3.zero)
                {
                    Vector3 displacement = match.endPosition - match.startPosition;
                    match.travelDirection = displacement.sqrMagnitude > 1e-6f ? displacement.normalized : Vector3.zero;
                }

                tracked.lastDetectionTimes[shape] = currentTime;
                tracked.lastMatches[shape] = match;

                onGestureMatched.Invoke(match);
                InvokeShapeEvents(shape, tracked.target);

                if (logDetections)
                {
                    Debug.Log($"[{nameof(GestureDetector)}] Gesture '{shape.ShapeId}' detected for '{tracked.target.name}'.");

                }
            }
        }

        /// <summary>
        /// Registers a new transform for gesture detection at runtime.
        /// </summary>
        public void Register(Transform target)
        {
            if (target == null || trackedObjects.Exists(t => t.target == target))
            {
                return;
            }

            trackedObjects.Add(new TrackedObject { target = target });
        }

        /// <summary>
        /// Removes a transform from gesture detection and clears its history.
        /// </summary>
        public void Unregister(Transform target)
        {
            trackedObjects.RemoveAll(t => t.target == target);
        }

        /// <summary>
        /// Clears the stored trail for a specific transform, forcing gesture detection to restart.
        /// </summary>
        public void ClearHistory(Transform target)
        {
            TrackedObject tracked = trackedObjects.Find(t => t.target == target);
            if (tracked != null)
            {
                tracked.samples.Clear();
                tracked.lastMatches.Clear();
                tracked.lastDetectionTimes.Clear();

            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!drawDebug || !Application.isPlaying)
            {
                return;
            }

            foreach (TrackedObject tracked in trackedObjects)
            {
                if (tracked == null || tracked.samples.Count < 2)
                {
                    continue;
                }

                Gizmos.color = trailColor;
                for (int i = 1; i < tracked.samples.Count; i++)
                {
                    Gizmos.DrawLine(tracked.samples[i - 1].position, tracked.samples[i].position);
                }

                foreach (KeyValuePair<GestureShape, GestureMatch> kvp in tracked.lastMatches)
                {
                    GestureShape shape = kvp.Key;
                    if (shape == null)
                    {
                        continue;
                    }

                    Gizmos.color = shape.GizmoColor;
                    shape.DrawGizmos(kvp.Value);
                }
            }
        }

#endif

        private void SampleObject(TrackedObject tracked, float currentTime)
        {
            Vector3 currentPosition = tracked.target.position;
            List<Sample> samples = tracked.samples;

            if (samples.Count == 0 || (currentPosition - samples[samples.Count - 1].position).sqrMagnitude >= minPointDistance * minPointDistance)
            {
                samples.Add(new Sample { position = currentPosition, time = currentTime });
            }
            for (int i = 0; i < samples.Count; i++)
            {
                if (currentTime - samples[i].time <= maxSampleAge)
                {
                    if (i > 0)
                    {
                        samples.RemoveRange(0, i);
                    }
                    return;
                }
            }

            samples.Clear();
            tracked.lastMatches.Clear();
        }

        private void InvokeShapeEvents(GestureShape shape, Transform target)
        {
            for (int i = 0; i < shapeEvents.Count; i++)
            {
                ShapeEvent binding = shapeEvents[i];
                if (binding?.shape == shape && binding.onDetected != null)
                {
                    binding.onDetected.Invoke(target);
                }
            }
        }

        internal static float CalculateTravelDistance(List<Sample> samples)
        {
            float travelledDistance = 0f;
            for (int i = 1; i < samples.Count; i++)
            {
                travelledDistance += Vector3.Distance(samples[i - 1].position, samples[i].position);
            }

            return travelledDistance;
        }

        internal static Vector3[] CopyPositions(List<Sample> samples)
        {
            Vector3[] result = new Vector3[samples.Count];
            for (int i = 0; i < samples.Count; i++)
            {
                result[i] = samples[i].position;
            }

            return result;
        }

        internal static Vector3 EstimateNormal(List<Sample> samples, Vector3 centroid)
        {
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < samples.Count; i++)
            {
                Vector3 current = samples[i].position - centroid;
                Vector3 next = samples[(i + 1) % samples.Count].position - centroid;
                normal += Vector3.Cross(current, next);
            }

            return normal;
        }

        internal static float CalculateAngularCoverage(List<float> angles)

        {
            angles.Sort();
            float maxGap = 0f;

            for (int i = 0; i < angles.Count - 1; i++)
            {
                float gap = angles[i + 1] - angles[i];
                if (gap > maxGap)
                {
                    maxGap = gap;
                }
            }

            if (angles.Count > 1)
            {
                float wrapGap = (angles[0] + Mathf.PI * 2f) - angles[angles.Count - 1];
                if (wrapGap > maxGap)
                {
                    maxGap = wrapGap;
                }
            }

            float coverage = Mathf.Rad2Deg * ((Mathf.PI * 2f) - maxGap);
            return Mathf.Clamp(coverage, 0f, 360f);
        }
    }
}
