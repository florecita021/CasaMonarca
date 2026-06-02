using System;

namespace CasaMonarcaApp.Models
{
    public class VolunteerLog
    {
        public string FullName { get; set; } = string.Empty;
        public string IdentificationNumber { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        
        // Changing to nullable types to ensure we catch empty form inputs safely
        public TimeSpan? TimeOfEntry { get; set; }
        public TimeSpan? TimeOfLeaving { get; set; }

        // Bulletproof calculation property
        public double HoursVolunteered 
        {
            get
            {
                // If either time field came through empty, return 0 hours instead of crashing
                if (!TimeOfEntry.HasValue || !TimeOfLeaving.HasValue)
                {
                    return 0;
                }

                if (TimeOfLeaving.Value >= TimeOfEntry.Value)
                {
                    return (TimeOfLeaving.Value - TimeOfEntry.Value).TotalHours;
                }
                
                // Accounts for night shifts crossing midnight safely
                return (TimeOfLeaving.Value.Add(TimeSpan.FromDays(1)) - TimeOfEntry.Value).TotalHours;
            }
        }
    }
}