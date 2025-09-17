using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace GestureRecognition
{
    /// <summary>
    /// Observes a <see cref="GestureDetector"/> and fires an event when a configured sequence of gestures is performed.
    /// </summary>
    public class GestureSequenceResponder : MonoBehaviour
    {
        [System.Serializable]
        private class Step
        {
            [Tooltip("Name used in the inspector to identify this step.")]
            public string label = "Step";

            [Tooltip("Gesture shape that must be detected at this step.")]
            public GestureShape shape;
        }

        [System.Serializable]
        public class SequenceCompletedEvent : UnityEvent<Transform>
        {
        }

        [SerializeField]
        [Tooltip("Detector that provides gesture match events.")]
        private GestureDetector detector;

        [SerializeField]
        [Tooltip("Ordered list of gestures that must be recognised.")]
        private List<Step> steps = new List<Step>();

        [SerializeField]
        [Tooltip("Maximum allowed time between recognised gestures (seconds). Set to 0 for unlimited.")]
        private float maxStepGap = 2f;

        [SerializeField]
        [Tooltip("If enabled every step must be performed by the same transform as the first one.")]
        private bool requireSameTarget = true;

        [SerializeField]
        [Tooltip("Restart the tracking progress automatically if the first step is detected mid sequence.")]
        private bool restartOnFirstMatch = true;

        [SerializeField]
        [Tooltip("If enabled the component logs whenever the sequence successfully completes.")]
        private bool logCompletion = true;

        [SerializeField]
        private SequenceCompletedEvent onSequenceCompleted = new SequenceCompletedEvent();

        private int currentIndex;
        private Transform activeTarget;
        private float lastStepTime = float.NegativeInfinity;
        private readonly List<GestureDetector.GestureMatch> currentMatches = new List<GestureDetector.GestureMatch>();
        private List<GestureDetector.GestureMatch> lastCompletedMatches = new List<GestureDetector.GestureMatch>();

        /// <summary>
        /// Event raised when the configured sequence is successfully completed.
        /// </summary>
        public SequenceCompletedEvent OnSequenceCompleted => onSequenceCompleted;

        /// <summary>
        /// The matches that completed the most recent successful sequence.
        /// </summary>
        public IReadOnlyList<GestureDetector.GestureMatch> LastCompletedMatches => lastCompletedMatches;

        private void OnEnable()
        {
            if (detector != null)
            {
                detector.OnGestureMatched.AddListener(HandleGesture);
            }
        }

        private void OnDisable()
        {
            if (detector != null)
            {
                detector.OnGestureMatched.RemoveListener(HandleGesture);
            }

            ResetProgress();
        }

        /// <summary>
        /// Clears any progress made towards the sequence.
        /// </summary>
        public void ResetSequence()
        {
            ResetProgress();
        }

        private void HandleGesture(GestureDetector.GestureMatch match)
        {
            if (steps.Count == 0 || match.shape == null)
            {
                return;
            }

            if (currentIndex == 0)
            {
                TryStartSequence(match);
                return;
            }

            if (requireSameTarget && match.target != activeTarget)
            {
                return;
            }

            if (maxStepGap > 0f && Time.time - lastStepTime > maxStepGap)
            {
                ResetProgress();
                TryStartSequence(match);
                return;
            }

            Step expected = steps[currentIndex];
            if (expected?.shape == null)
            {
                ResetProgress();
                return;
            }

            if (expected.shape == match.shape)
            {
                AcceptStep(match);
                return;
            }

            if (restartOnFirstMatch && steps[0]?.shape == match.shape)
            {
                StartSequence(match);
                return;
            }

            ResetProgress();
        }

        private void TryStartSequence(GestureDetector.GestureMatch match)
        {
            Step first = steps[0];
            if (first?.shape == null)
            {
                return;
            }

            if (first.shape == match.shape)
            {
                StartSequence(match);
            }
        }

        private void StartSequence(GestureDetector.GestureMatch match)
        {
            currentMatches.Clear();
            currentMatches.Add(match);
            activeTarget = match.target;
            lastStepTime = Time.time;
            currentIndex = Mathf.Min(1, steps.Count);

            if (steps.Count <= 1)
            {
                CompleteSequence();
            }
        }

        private void AcceptStep(GestureDetector.GestureMatch match)
        {
            currentMatches.Add(match);
            activeTarget = requireSameTarget ? activeTarget : match.target;
            lastStepTime = Time.time;
            currentIndex++;

            if (currentIndex >= steps.Count)
            {
                CompleteSequence();
            }
        }

        private void CompleteSequence()
        {
            lastCompletedMatches = new List<GestureDetector.GestureMatch>(currentMatches);
            Transform target = activeTarget != null ? activeTarget : (lastCompletedMatches.Count > 0 ? lastCompletedMatches[lastCompletedMatches.Count - 1].target : null);
            if (logCompletion)
            {
                string targetName = target != null ? $" '{target.name}'" : string.Empty;
                Debug.Log($"[{nameof(GestureSequenceResponder)}] Sequence completed{targetName}.");
            }

            onSequenceCompleted.Invoke(target);
            ResetProgress();
        }

        private void ResetProgress()
        {
            currentIndex = 0;
            activeTarget = null;
            lastStepTime = float.NegativeInfinity;
            currentMatches.Clear();
        }
    }
}
