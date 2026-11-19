using System;
using System.Collections.Generic;
using System.Linq;

namespace SpineRecovery.App
{
    [Serializable]
    public sealed class FixedTrackerBinding
    {
        public int slot;
        public string macAddress;
        public int sensorId;
        public string bodyPosition;
        public string displayName;
        public bool enabled = true;

        public bool HasMac => !string.IsNullOrWhiteSpace(macAddress);

        public string NormalizedMac => (macAddress ?? string.Empty).Trim().ToUpperInvariant();
    }

    [Serializable]
    public sealed class FixedTrackerBindingSet
    {
        public string phoneDeviceId;
        public int requiredTrackerCount = FixedTrackerBindingRepository.DefaultTrackerCount;
        public int version = 1;
        public bool locked;
        public string updatedAt;
        public FixedTrackerBinding[] trackers = Array.Empty<FixedTrackerBinding>();

        public IEnumerable<FixedTrackerBinding> EnabledBindings =>
            (trackers ?? Array.Empty<FixedTrackerBinding>()).Where(x => x != null && x.enabled && x.HasMac);

        public bool IsComplete => EnabledBindings.Count() == requiredTrackerCount &&
            EnabledBindings.Select(x => x.NormalizedMac).Distinct().Count() == requiredTrackerCount;
    }

    /// <summary>
    /// Phone-scoped administrator provisioning. Patient flows only read this snapshot;
    /// replacement and locking are exposed to the administrator flow added next.
    /// </summary>
    public sealed class FixedTrackerBindingRepository
    {
        public const int DefaultTrackerCount = 10;
        private const string FileName = "spine-fixed-tracker-bindings.json";

        [Serializable]
        private sealed class Document
        {
            public FixedTrackerBindingSet binding;
        }

        private FixedTrackerBindingSet current;

        public FixedTrackerBindingSet Load()
        {
            if (current != null) return current;
            if (JsonFileStore.TryLoad(FileName, out Document document) && document?.binding != null)
            {
                current = Normalize(document.binding);
                return current;
            }

            current = CreateEmpty();
            Save(current);
            return current;
        }

        public void Save(FixedTrackerBindingSet binding)
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            current = Normalize(binding);
            current.updatedAt = DateTimeOffset.UtcNow.ToString("O");
            JsonFileStore.Save(FileName, new Document { binding = current });
        }

        public static FixedTrackerBindingSet CreateEmpty(string phoneDeviceId = null)
        {
            var slots = new FixedTrackerBinding[DefaultTrackerCount];
            for (int i = 0; i < slots.Length; i++)
                slots[i] = new FixedTrackerBinding { slot = i + 1, bodyPosition = DefaultBodyPositions[i] };
            return new FixedTrackerBindingSet
            {
                phoneDeviceId = phoneDeviceId ?? string.Empty,
                requiredTrackerCount = DefaultTrackerCount,
                trackers = slots,
                updatedAt = DateTimeOffset.UtcNow.ToString("O"),
            };
        }

        private static FixedTrackerBindingSet Normalize(FixedTrackerBindingSet value)
        {
            value.trackers = (value.trackers ?? Array.Empty<FixedTrackerBinding>())
                .Where(x => x != null)
                .OrderBy(x => x.slot)
                .ToArray();
            value.requiredTrackerCount = Math.Max(1, Math.Min(DefaultTrackerCount, value.requiredTrackerCount));
            return value;
        }

        public static readonly string[] DefaultBodyPositions =
        {
            "胸部", "腰部", "左大腿", "右大腿", "左小腿",
            "右小腿", "左脚", "右脚", "左肩", "右肩",
        };
    }
}
