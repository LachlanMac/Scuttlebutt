using UnityEngine;
using System;

namespace Starbelter.Strategic
{
    /// <summary>
    /// Static galactic time manager.
    /// 1 real minute = 1 game hour
    /// 24 game hours = 1 game day
    /// 7 game days = 1 game week
    /// </summary>
    public class GalacticTime : MonoBehaviour
    {
        public static GalacticTime Instance { get; private set; }

        [Header("Time Settings")]
        [SerializeField] private float realSecondsPerGameHour = 60f; // 1 minute = 1 hour
        [SerializeField] private bool pauseTime = false;

        /// <summary>
        /// Real seconds per game hour (for syncing other systems).
        /// </summary>
        public static float SecondsPerHour => Instance?.realSecondsPerGameHour ?? 60f;

        [Header("Current Time (Read Only)")]
        [SerializeField] private int currentWeek = 1;
        [SerializeField] private int currentDay = 1;   // 1-7
        [SerializeField] private int currentHour = 0;  // 0-23
        [SerializeField] private float currentMinute = 0f; // 0-59.99

        // Time tracking
        private float hourAccumulator = 0f;

        // Events
        public static event Action<int> OnMinuteChanged;      // game minute
        public static event Action<int> OnHourChanged;        // game hour (0-23)
        public static event Action<int> OnDayChanged;         // game day (1-7)
        public static event Action<int> OnWeekChanged;        // game week
        public static event Action<GalacticTimestamp> OnTimeChanged; // every tick

        // Properties
        public static int Week => Instance?.currentWeek ?? 1;
        public static int Day => Instance?.currentDay ?? 1;
        public static int Hour => Instance?.currentHour ?? 0;
        public static float Minute => Instance?.currentMinute ?? 0f;
        public static bool IsPaused => Instance?.pauseTime ?? false;

        public static string DayName => Day switch
        {
            1 => "Monday",
            2 => "Tuesday",
            3 => "Wednesday",
            4 => "Thursday",
            5 => "Friday",
            6 => "Saturday",
            7 => "Sunday",
            _ => "Unknown"
        };

        /// <summary>
        /// Get current time as a timestamp.
        /// </summary>
        public static GalacticTimestamp Now => new GalacticTimestamp(Week, Day, Hour, (int)Minute);

        /// <summary>
        /// Total game hours elapsed since start.
        /// </summary>
        public static float TotalHours => Instance != null
            ? ((Instance.currentWeek - 1) * 7 * 24) +
              ((Instance.currentDay - 1) * 24) +
              Instance.currentHour +
              (Instance.currentMinute / 60f)
            : 0f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (pauseTime) return;

            float deltaHours = Time.deltaTime / realSecondsPerGameHour;
            AdvanceTime(deltaHours);
        }

        private void AdvanceTime(float hours)
        {
            int previousMinute = (int)currentMinute;
            int previousHour = currentHour;
            int previousDay = currentDay;
            int previousWeek = currentWeek;

            // Add time
            currentMinute += hours * 60f;

            // Roll over minutes → hours
            while (currentMinute >= 60f)
            {
                currentMinute -= 60f;
                currentHour++;
            }

            // Roll over hours → days
            while (currentHour >= 24)
            {
                currentHour -= 24;
                currentDay++;
            }

            // Roll over days → weeks
            while (currentDay > 7)
            {
                currentDay -= 7;
                currentWeek++;
            }

            // Fire events
            OnTimeChanged?.Invoke(Now);

            if ((int)currentMinute != previousMinute)
            {
                OnMinuteChanged?.Invoke((int)currentMinute);
            }

            if (currentHour != previousHour)
            {
                OnHourChanged?.Invoke(currentHour);
            }

            if (currentDay != previousDay)
            {
                OnDayChanged?.Invoke(currentDay);
                Debug.Log($"[GalacticTime] New day: {DayName} (Week {currentWeek})");
            }

            if (currentWeek != previousWeek)
            {
                OnWeekChanged?.Invoke(currentWeek);
                Debug.Log($"[GalacticTime] New week: {currentWeek}");
            }
        }

        /// <summary>
        /// Set time directly (for testing/save loading).
        /// </summary>
        public static void SetTime(int week, int day, int hour, int minute = 0)
        {
            if (Instance == null) return;

            Instance.currentWeek = Mathf.Max(1, week);
            Instance.currentDay = Mathf.Clamp(day, 1, 7);
            Instance.currentHour = Mathf.Clamp(hour, 0, 23);
            Instance.currentMinute = Mathf.Clamp(minute, 0, 59);

            Debug.Log($"[GalacticTime] Time set to Week {Week}, {DayName} {Hour:D2}:{(int)Minute:D2}");
        }

        /// <summary>
        /// Pause/unpause time.
        /// </summary>
        public static void SetPaused(bool paused)
        {
            if (Instance != null)
            {
                Instance.pauseTime = paused;
            }
        }

        /// <summary>
        /// Skip forward by a number of game hours.
        /// </summary>
        public static void SkipHours(float hours)
        {
            Instance?.AdvanceTime(hours);
        }

        /// <summary>
        /// Calculate hours until a specific time.
        /// </summary>
        public static float HoursUntil(int targetDay, int targetHour)
        {
            float currentTotal = (Day - 1) * 24 + Hour + (Minute / 60f);
            float targetTotal = (targetDay - 1) * 24 + targetHour;

            if (targetTotal <= currentTotal)
            {
                // Target is next week
                targetTotal += 7 * 24;
            }

            return targetTotal - currentTotal;
        }

        /// <summary>
        /// Check if current time is within a range (same day).
        /// </summary>
        public static bool IsHourBetween(int startHour, int endHour)
        {
            if (startHour <= endHour)
            {
                return Hour >= startHour && Hour < endHour;
            }
            else
            {
                // Wraps around midnight
                return Hour >= startHour || Hour < endHour;
            }
        }

        /// <summary>
        /// Get a formatted time string.
        /// </summary>
        public static string FormattedTime => $"Week {Week}, {DayName} {Hour:D2}:{(int)Minute:D2}";
    }

    /// <summary>
    /// Represents a specific point in galactic time.
    /// </summary>
    [System.Serializable]
    public struct GalacticTimestamp
    {
        public int Week;
        public int Day;
        public int Hour;
        public int Minute;

        public GalacticTimestamp(int week, int day, int hour, int minute = 0)
        {
            Week = week;
            Day = day;
            Hour = hour;
            Minute = minute;
        }

        public float TotalHours => ((Week - 1) * 7 * 24) + ((Day - 1) * 24) + Hour + (Minute / 60f);

        public static float operator -(GalacticTimestamp a, GalacticTimestamp b)
        {
            return a.TotalHours - b.TotalHours;
        }

        public static bool operator >(GalacticTimestamp a, GalacticTimestamp b)
        {
            return a.TotalHours > b.TotalHours;
        }

        public static bool operator <(GalacticTimestamp a, GalacticTimestamp b)
        {
            return a.TotalHours < b.TotalHours;
        }

        public static bool operator >=(GalacticTimestamp a, GalacticTimestamp b)
        {
            return a.TotalHours >= b.TotalHours;
        }

        public static bool operator <=(GalacticTimestamp a, GalacticTimestamp b)
        {
            return a.TotalHours <= b.TotalHours;
        }

        public override string ToString()
        {
            string dayName = Day switch
            {
                1 => "Mon", 2 => "Tue", 3 => "Wed", 4 => "Thu",
                5 => "Fri", 6 => "Sat", 7 => "Sun", _ => "???"
            };
            return $"W{Week} {dayName} {Hour:D2}:{Minute:D2}";
        }
    }
}
