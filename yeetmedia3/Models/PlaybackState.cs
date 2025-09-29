using System;

namespace Yeetmedia3.Models
{
    public class PlaybackState
    {
        public int EpisodeNumber { get; set; }
        public double Position { get; set; } // in seconds
        public DateTime LastUpdated { get; set; }
        public double Duration { get; set; } // total duration in seconds
        public bool IsPlaying { get; set; }
        public string? EpisodeTitle { get; set; }
        public string? DeviceId { get; set; } // To track which device last updated
        public string? DeviceName { get; set; }

        // Formatted position for readability
        public string PositionFormatted => FormatTime(Position);
        public string DurationFormatted => FormatTime(Duration);

        private static string FormatTime(double seconds)
        {
            if (seconds < 0) return "0:00:00";
            var timeSpan = TimeSpan.FromSeconds(seconds);
            return $"{(int)timeSpan.TotalHours}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
        }
    }
}