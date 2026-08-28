#if BHAPTICS_CONTACT_COMPRESSOR
using System;
using System.Collections.Generic;
using System.IO;
using bHapticsLib;
using Furroxide.ContactCompressor;

namespace bHapticsOSC
{
    /// <summary>
    /// Drives motors from a consolidated avatar.
    ///
    /// A compressed avatar no longer sends one boolean per motor. It sends a handful of floats per
    /// body region that encode <em>where</em> it was touched, and this turns that position back
    /// into motor intensities using the layout in the manifest.
    ///
    /// Two things fall out of that which the per-motor path cannot do:
    /// contact spreads smoothly across neighbouring motors instead of snapping to one, and a motor
    /// left on by a dropped OSC message heals on the next tick, because every tick writes every
    /// motor rather than only the ones that changed.
    ///
    /// Inactive unless a manifest is present, so an uncompressed avatar is unaffected.
    /// </summary>
    internal class ContactCompression
    {
        internal const string ManifestFileName = "contact-compressor.json";

        /// <summary>How many motors one contact is allowed to spread over. 4 gives bilinear-like falloff on a grid.</summary>
        private const int MaxPointsPerContact = 4;

        private readonly ContactCompressorManifest _manifest;
        private readonly ContactCompressorDecoder _decoder;

        /// <summary>Manifest point id -> the motor it drives. Built once; the layout does not change at runtime.</summary>
        private readonly Dictionary<string, MotorRef> _motors = new Dictionary<string, MotorRef>(StringComparer.Ordinal);

        private readonly List<string> _addresses = new List<string>();

        private struct MotorRef
        {
            internal PositionID Position;
            internal int Node;              // 1-based, matching the rest of the app
        }

        private ContactCompression(ContactCompressorManifest manifest)
        {
            _manifest = manifest;
            _decoder = new ContactCompressorDecoder(manifest);
        }

        internal int RegionCount => _manifest.regions.Count;
        internal int MotorCount => _motors.Count;
        internal IReadOnlyList<string> OscAddresses => _addresses;

        /// <summary>
        /// Loads the manifest beside the config files, or returns null when there is none - which is
        /// simply how an unconsolidated setup presents itself.
        /// </summary>
        internal static ContactCompression TryLoad(string configFolder, IDictionary<string, PositionID> deviceNames)
        {
            // A missing folder means the same thing as a missing manifest: nothing to load. This
            // guard exists because the one time this was handed null - Program's initialization
            // order handing VRChatSupport a not-yet-set ConfigFolder - the Path.Combine below took
            // the whole process down before Main ran, and a config lookup should never be able to
            // do that again whatever the caller got wrong.
            if (string.IsNullOrEmpty(configFolder))
                return null;

            string path = Path.Combine(configFolder, ManifestFileName);
            if (!File.Exists(path))
                return null;

            ContactCompressorManifest manifest;
            try
            {
                manifest = ManifestJson.Parse(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ContactCompressor] Could not read {path}: {e.Message}");
                return null;
            }

            var compression = new ContactCompression(manifest);
            compression.MapMotors(deviceNames);
            compression.CollectAddresses();

            if (compression._motors.Count == 0)
            {
                Console.WriteLine($"[ContactCompressor] {path} has no points matching any known device; ignoring it.");
                return null;
            }

            return compression;
        }

        /// <summary>
        /// Resolves each manifest point id - "VestFront/7", "ForearmL/2" - to a device and motor.
        /// The names are the same ones the v2 parameters use, so this needs no new table.
        /// </summary>
        private void MapMotors(IDictionary<string, PositionID> deviceNames)
        {
            var unknown = new HashSet<string>(StringComparer.Ordinal);

            foreach (ContactRegionManifest region in _manifest.regions)
            {
                if (region?.points == null) continue;

                foreach (ContactPointManifest point in region.points)
                {
                    if (point == null || string.IsNullOrWhiteSpace(point.id)) continue;

                    int slash = point.id.LastIndexOf('/');
                    if (slash <= 0 || slash == point.id.Length - 1) continue;

                    string device = point.id.Substring(0, slash);
                    if (!deviceNames.TryGetValue(device, out PositionID position))
                    {
                        unknown.Add(device);
                        continue;
                    }

                    if (!int.TryParse(point.id.Substring(slash + 1), out int node) || node < 0)
                        continue;

                    // Manifest nodes are 0-based, matching the v2 parameter names; the app is 1-based.
                    _motors[point.id] = new MotorRef { Position = position, Node = node + 1 };
                }
            }

            foreach (string name in unknown)
                Console.WriteLine($"[ContactCompressor] Manifest references unknown device '{name}'; its points are ignored.");
        }

        private void CollectAddresses()
        {
            foreach (ContactRegionManifest region in _manifest.regions)
            {
                if (region == null || string.IsNullOrWhiteSpace(region.id)) continue;

                EncoderAxes axes = region.ParsedAxes;
                for (int axis = 0; axis < 3; axis++)
                {
                    EncoderAxes flag = axis == 0 ? EncoderAxes.X : axis == 1 ? EncoderAxes.Y : EncoderAxes.Z;
                    if ((axes & flag) == 0) continue;

                    _addresses.Add(ContactParameterNames.OscAddress(_manifest.prefix, region.id, axis, true));
                    _addresses.Add(ContactParameterNames.OscAddress(_manifest.prefix, region.id, axis, false));
                }
            }
        }

        /// <summary>Feeds one OSC value in. Safe to call from the OSC dispatch thread.</summary>
        internal void Accept(string address, float value) => _decoder.Accept(address, value);

        internal void Reset() => _decoder.Reset();

        /// <summary>
        /// Writes this tick's motor intensities. Called from the haptics thread.
        /// </summary>
        /// <param name="setNodeIntensity">(position, node, intensity, source)</param>
        /// <param name="intensityFor">Configured intensity percentage for a device.</param>
        internal void Apply(Action<PositionID, int, int, string> setNodeIntensity, Func<PositionID, int> intensityFor)
        {
            foreach (ContactRegionManifest region in _manifest.regions)
            {
                if (region?.points == null || region.points.Count == 0) continue;

                string source = "v3/" + region.id;

                var weights = new Dictionary<string, float>(StringComparer.Ordinal);
                float peak = 0f;

                foreach (WeightedPoint weighted in _decoder.Sample(region.id, MaxPointsPerContact))
                {
                    weights[weighted.Id] = weighted.Weight;
                    if (weighted.Weight > peak) peak = weighted.Weight;
                }

                // Every point is written every tick, including the untouched ones at zero. That is
                // what makes a dropped OSC message self-correcting rather than leaving a motor
                // buzzing until the avatar changes.
                foreach (ContactPointManifest point in region.points)
                {
                    if (!_motors.TryGetValue(point.id, out MotorRef motor)) continue;

                    int intensity = 0;
                    if (peak > 0f && weights.TryGetValue(point.id, out float weight))
                    {
                        // Normalise to the strongest point so a light touch still reaches the
                        // configured intensity; the weights carry the shape, not the loudness.
                        intensity = (int)Math.Round(intensityFor(motor.Position) * (weight / peak));
                    }

                    setNodeIntensity(motor.Position, motor.Node, intensity, source);
                }
            }
        }

        internal void PrintSummary()
        {
            Console.WriteLine($"[ContactCompressor] Loaded {RegionCount} region(s) driving {MotorCount} motor(s) "
                              + $"from {_addresses.Count} parameter(s).");

            foreach (ContactRegionManifest region in _manifest.regions)
                Console.WriteLine($"[ContactCompressor]   {region.id}: {region.points.Count} points, axes {region.axes}");
        }
    }
}
#endif
