using System;
using System.Collections.Generic;
using bHapticsLib;
using OscLib;
using OscLib.Utils;
using OscLib.VRChat;
using Rug.Osc;
using System.Collections.Concurrent;
using System.Threading;
using OscLib.VRChat.Attributes;

namespace bHapticsOSC
{
    internal class VRChatSupport : ThreadedTask
    {
        private bool ShouldRun;
        private Dictionary<PositionID, Device> Devices = new Dictionary<PositionID, Device>();
        private bool AFK;
        private bool InStation;
        private bool Seated;
        private int UdonAudioLink;
        private bool PunchEnabledMenuSet;
        private bool PunchEnabledMenuValue = true;
        private bool PunchRippleMenuSet;
        private bool PunchRippleMenuValue = true;
        private bool PunchStrengthMenuSet;
        private float PunchStrengthMenuValue = 1f;
        private bool PunchDurationMenuSet;
        private float PunchDurationMenuValue = 1f;

        private const string AvatarParameterPrefix = "/avatar/parameters";
        private const string PunchParameterPrefix = AvatarParameterPrefix + "/bOSC/v2/Punch";
        private const int PunchBandLight = 1;
        private const int PunchBandHard = 2;
        private const int PunchMergeWindowMs = 150;
        private const int VestColumns = 4;
        private const int VestNodeCount = 20;
        private const int PunchMaxPulses = 256;
        private static Tuple<int, PositionID, string, string>[] DeviceSchemes = new Tuple<int, PositionID, string, string>[]
        {
            new Tuple<int, PositionID, string, string>(6, PositionID.Head, "bHapticsOSC_Head", string.Empty),

            new Tuple<int, PositionID, string, string>(20, PositionID.VestFront, "bHapticsOSC_Vest_Front", string.Empty),
            new Tuple<int, PositionID, string, string>(20, PositionID.VestBack, "bHapticsOSC_Vest_Back", string.Empty),

            new Tuple<int, PositionID, string, string>(6, PositionID.ArmLeft, "bHapticsOSC_Arm_Left", string.Empty),
            new Tuple<int, PositionID, string, string>(6, PositionID.ArmRight, "bHapticsOSC_Arm_Right", string.Empty),

            new Tuple<int, PositionID, string, string>(3, PositionID.HandLeft, "bHapticsOSC_Hand_Left", string.Empty),
            new Tuple<int, PositionID, string, string>(3, PositionID.HandRight, "bHapticsOSC_Hand_Right", string.Empty),

            //new Tuple<int, PositionID, string, string>(0, PositionID.GloveLeft, "bHapticsOSC_Glove_Left", string.Empty),
            //new Tuple<int, PositionID, string, string>(0, PositionID.GloveRight, "bHapticsOSC_Glove_Right", string.Empty),

            new Tuple<int, PositionID, string, string>(3, PositionID.FootLeft, "bHapticsOSC_Foot_Left", string.Empty),
            new Tuple<int, PositionID, string, string>(3, PositionID.FootRight, "bHapticsOSC_Foot_Right", string.Empty),
        };

        private class VRChatPacket { }

        private class VRChatPacket_Node : VRChatPacket
        {
            internal PositionID position;
            internal int node;
            internal int intensity;
        }
        private ConcurrentQueue<VRChatPacket> PacketQueue = new ConcurrentQueue<VRChatPacket>();
        private List<PunchPulse> PunchPulses = new List<PunchPulse>();

        private class VRChatPacket_int : VRChatPacket { internal int value; }
        private class VRChatPacket_bool : VRChatPacket { internal bool value; }
        private class VRChatPacket_float : VRChatPacket { internal float value; }
        private class VRChatPacket_string : VRChatPacket { internal string value; }

        private class VRChatPacket_AvatarChange : VRChatPacket_string { }
        private class VRChatPacket_AFK : VRChatPacket_bool { }
        private class VRChatPacket_InStation : VRChatPacket_bool { }
        private class VRChatPacket_Seated : VRChatPacket_bool { }
        private class VRChatPacket_UdonAudioLink : VRChatPacket_int { }
        private class VRChatPacket_Punch : VRChatPacket
        {
            internal PositionID position;
            internal int node;
            internal int band;
        }
        private class VRChatPacket_PunchEnabled : VRChatPacket_bool { }
        private class VRChatPacket_PunchRipple : VRChatPacket_bool { }
        private class VRChatPacket_PunchStrength : VRChatPacket_float { }
        private class VRChatPacket_PunchDuration : VRChatPacket_float { }

        private class PunchPulse
        {
            internal PositionID Position;
            internal int Node;
            internal int Band;
            internal long StartMs;
            internal int Intensity;
            internal int DurationMs;
        }

        internal VRChatSupport() : base()
        {
            foreach (Tuple<int, PositionID, string, string> device in DeviceSchemes)
            {
                if (device.Item1 <= 0)
                    continue;
                Devices[device.Item2] = new Device(device.Item2);

                string[] nodeAddressesArr = new string[device.Item1];
                for (int i = 1; i < device.Item1 + 1; i++)
                    nodeAddressesArr[i - 1] = $"{AvatarParameterPrefix}/{device.Item3}_{i}";

                switch (device.Item2)
                {
                    case PositionID.VestFront:
                        Array.Reverse(nodeAddressesArr, 0, 4);
                        Array.Reverse(nodeAddressesArr, 4, 4);
                        Array.Reverse(nodeAddressesArr, 8, 4);
                        Array.Reverse(nodeAddressesArr, 12, 4);
                        Array.Reverse(nodeAddressesArr, 16, 4);
                        break;

                    case PositionID.Head:
                        Array.Reverse(nodeAddressesArr, 0, 6);
                        break;

                    case PositionID.FootRight:
                        Array.Reverse(nodeAddressesArr, 0, 3);
                        break;

                    default:
                        break;
                }

                for (int i = 0; i < nodeAddressesArr.Length; i++)
                {
                    string path = nodeAddressesArr[i];
                    int index = i + 1;
                    OscManager.Attach(path, (OscMessage msg) => OnNode(msg, index, device.Item2));
                    OscManager.Attach($"{path}_int", (OscMessage msg) => OnNodeIntensity(msg, index, device.Item2));
                    OscManager.Attach($"{path.Replace("bHapticsOSC_", "bHaptics_")}_bool", (OscMessage msg) => OnNode(msg, index, device.Item2));
                }
            }

            AttachPunchParameters();
        }

        public override bool BeginInitInternal()
        {
            if (ShouldRun)
                EndInit();

            ShouldRun = true;
            return true;
        }

        public override void WithinThread()
        {
            while (ShouldRun)
            {
                while (PacketQueue.TryDequeue(out VRChatPacket packet))
                {
                    if (packet is VRChatPacket_AFK)
                        AFK = ((VRChatPacket_AFK)packet).value;
                    else if (packet is VRChatPacket_InStation)
                        InStation = ((VRChatPacket_InStation)packet).value;
                    else if (packet is VRChatPacket_Seated)
                        Seated = ((VRChatPacket_Seated)packet).value;
                    else if (packet is VRChatPacket_UdonAudioLink)
                        UdonAudioLink = ((VRChatPacket_UdonAudioLink)packet).value;
                    else if (packet is VRChatPacket_PunchEnabled)
                    {
                        PunchEnabledMenuSet = true;
                        PunchEnabledMenuValue = ((VRChatPacket_PunchEnabled)packet).value;
                    }
                    else if (packet is VRChatPacket_PunchRipple)
                    {
                        PunchRippleMenuSet = true;
                        PunchRippleMenuValue = ((VRChatPacket_PunchRipple)packet).value;
                    }
                    else if (packet is VRChatPacket_PunchStrength)
                    {
                        PunchStrengthMenuSet = true;
                        PunchStrengthMenuValue = ((VRChatPacket_PunchStrength)packet).value.Clamp(0f, 1f);
                    }
                    else if (packet is VRChatPacket_PunchDuration)
                    {
                        PunchDurationMenuSet = true;
                        PunchDurationMenuValue = ((VRChatPacket_PunchDuration)packet).value.Clamp(0f, 1f);
                    }
                    else if (packet is VRChatPacket_AvatarChange)
                    {
                        ResetDevices();
                        PunchPulses.Clear();
                        if (Program.VRChat.avatarOSCConfigReset.Value.Enabled)
                            VRCAvatarConfig.RemoveFile(((VRChatPacket_AvatarChange)packet).value);
                    }
                    else if (packet is VRChatPacket_Punch)
                    {
                        VRChatPacket_Punch punchPacket = (VRChatPacket_Punch)packet;
                        RegisterPunch(punchPacket.position, punchPacket.node, punchPacket.band);
                    }
                    else if (packet is VRChatPacket_Node)
                    {
                        VRChatPacket_Node nodePacket = (VRChatPacket_Node)packet;
                        SetDeviceNodeIntensity(nodePacket.position, nodePacket.node, nodePacket.intensity);
                    }
                }

                ExpirePunchPulses(NowMs());
                SubmitDevices();

                if (ShouldRun)
                    Thread.Sleep(100);
            }
        }

        public override bool EndInitInternal()
        {
            ShouldRun = false;
            while (IsAlive()) { Thread.Sleep(100); }
            return true;
        }

        private static void AttachPunchParameters()
        {
            AttachPunchPanel(PositionID.VestFront, "VestFront");
            AttachPunchPanel(PositionID.VestBack, "VestBack");

            OscManager.Attach($"{PunchParameterPrefix}/Enabled", OnPunchEnabled);
            OscManager.Attach($"{PunchParameterPrefix}/Ripple", OnPunchRipple);
            OscManager.Attach($"{PunchParameterPrefix}/Strength", OnPunchStrength);
            OscManager.Attach($"{PunchParameterPrefix}/Duration", OnPunchDuration);
        }

        private static void AttachPunchPanel(PositionID position, string panel)
        {
            for (int node = 0; node < VestNodeCount; node++)
            {
                int bufferNode = node + 1;
                OscManager.Attach($"{PunchParameterPrefix}/{panel}/{node}/Light", (OscMessage msg) => OnPunchNode(msg, bufferNode, position, PunchBandLight));
                OscManager.Attach($"{PunchParameterPrefix}/{panel}/{node}/Hard", (OscMessage msg) => OnPunchNode(msg, bufferNode, position, PunchBandHard));
            }
        }

        [VRC_AFK]
        private static void OnAFK(bool status)
            => Program.VRCSupport?.PacketQueue.Enqueue(new VRChatPacket_AFK { value = status });

        [VRC_InStation]
        private static void OnInStation(bool status)
            => Program.VRCSupport?.PacketQueue.Enqueue(new VRChatPacket_InStation { value = status });

        [VRC_Seated]
        private static void OnSeated(bool status)
            => Program.VRCSupport?.PacketQueue.Enqueue(new VRChatPacket_Seated { value = status });

        [VRC_AvatarChange]
        private static void OnAvatarChange(string avatarId)
        {
            Console.WriteLine($"Avatar Changed to {avatarId}");
            Program.VRCSupport?.PacketQueue.Enqueue(new VRChatPacket_AvatarChange { value = avatarId });
        }

        [VRC_AvatarParameter("bHapticsOSC_UdonAudioLink")]
        private void OnUdonAudioLink(int amplitude)
            => Program.VRCSupport?.PacketQueue.Enqueue(new VRChatPacket_UdonAudioLink { value = amplitude });

        private static void OnPunchNode(OscMessage msg, int node, PositionID position, int band)
        {
            if (!TryReadBool(msg, out bool value) || !value)
                return;

            Program.VRCSupport?.PacketQueue.Enqueue(new VRChatPacket_Punch
            {
                position = position,
                node = node,
                band = band
            });
        }

        private static void OnPunchEnabled(OscMessage msg)
        {
            if (TryReadBool(msg, out bool value))
                Program.VRCSupport?.PacketQueue.Enqueue(new VRChatPacket_PunchEnabled { value = value });
        }

        private static void OnPunchRipple(OscMessage msg)
        {
            if (TryReadBool(msg, out bool value))
                Program.VRCSupport?.PacketQueue.Enqueue(new VRChatPacket_PunchRipple { value = value });
        }

        private static void OnPunchStrength(OscMessage msg)
        {
            if (TryReadFloat(msg, out float value))
                Program.VRCSupport?.PacketQueue.Enqueue(new VRChatPacket_PunchStrength { value = value });
        }

        private static void OnPunchDuration(OscMessage msg)
        {
            if (TryReadFloat(msg, out float value))
                Program.VRCSupport?.PacketQueue.Enqueue(new VRChatPacket_PunchDuration { value = value });
        }

        private static void OnNode(OscMessage msg, int node, PositionID position)
        {
            if ((msg == null) || (!(msg[0] is bool)))
                return;
            Program.VRCSupport?.PacketQueue.Enqueue(new VRChatPacket_Node
            {
                position = position,
                node = node,
                intensity = ((bool)msg[0]) ? Program.Devices.PositionIDToIntensity(position) : 0,
            });
        }

        private static void OnNodeIntensity(OscMessage msg, int node, PositionID position)
        {
            if ((msg == null) || (!(msg[0] is int)))
                return;
            Program.VRCSupport?.PacketQueue.Enqueue(new VRChatPacket_Node
            {
                position = position,
                node = node,
                intensity = (int)msg[0],
            });
        }

        private static bool TryReadBool(OscMessage msg, out bool value)
        {
            value = false;
            if (msg == null || msg.Count <= 0)
                return false;

            if (msg[0] is bool boolValue)
            {
                value = boolValue;
                return true;
            }

            if (msg[0] is int intValue)
            {
                value = intValue != 0;
                return true;
            }

            if (msg[0] is float floatValue)
            {
                value = floatValue > 0f;
                return true;
            }

            return false;
        }

        private static bool TryReadFloat(OscMessage msg, out float value)
        {
            value = 0f;
            if (msg == null || msg.Count <= 0)
                return false;

            if (msg[0] is float floatValue)
            {
                value = floatValue;
                return true;
            }

            if (msg[0] is int intValue)
            {
                value = intValue;
                return true;
            }

            if (msg[0] is bool boolValue)
            {
                value = boolValue ? 1f : 0f;
                return true;
            }

            return false;
        }

        private void SubmitDevices()
        {
            if ((AFK && !Program.VRChat.reactivity.Value.AFK) 
                || (InStation && !Program.VRChat.reactivity.Value.InStation) 
                || (Seated && !Program.VRChat.reactivity.Value.Seated)
                || (Devices.Count <= 0))
                return;

            foreach (Device device in Devices.Values)
                device.Submit(BuildPunchOverlay(device.Position));
        }

        private void ResetDevices()
        {
            if (Devices.Count <= 0)
                return;
            foreach (Device device in Devices.Values)
                device.Reset();
        }

        private void RegisterPunch(PositionID position, int node, int band)
        {
            if (!IsPunchEnabled() || node < 1 || node > VestNodeCount)
                return;

            long nowMs = NowMs();
            int durationMs = GetPunchDurationMs(band);
            int intensity = GetPunchIntensity(band);

            foreach (PunchPulse pulse in PunchPulses)
            {
                if (pulse.Position != position || pulse.Node != node)
                    continue;

                if (nowMs - pulse.StartMs > PunchMergeWindowMs)
                    continue;

                pulse.Band = System.Math.Max(pulse.Band, band);
                pulse.Intensity = System.Math.Max(pulse.Intensity, intensity);
                pulse.DurationMs = System.Math.Max(pulse.DurationMs, durationMs);
                pulse.StartMs = nowMs;
                return;
            }

            if (PunchPulses.Count >= PunchMaxPulses)
                return;

            PunchPulses.Add(new PunchPulse
            {
                Position = position,
                Node = node,
                Band = band,
                StartMs = nowMs,
                Intensity = intensity,
                DurationMs = durationMs
            });
        }

        private byte[] BuildPunchOverlay(PositionID position)
        {
            if (!IsPunchEnabled() || (position != PositionID.VestFront && position != PositionID.VestBack) || PunchPulses.Count <= 0)
                return null;

            long nowMs = NowMs();
            byte[] overlay = null;
            bool rippleEnabled = IsPunchRippleEnabled();

            foreach (PunchPulse pulse in PunchPulses)
            {
                if (pulse.Position != position)
                    continue;

                for (int node = 1; node <= VestNodeCount; node++)
                {
                    int intensity = CalculatePunchNodeIntensity(pulse, node, nowMs, rippleEnabled);
                    if (intensity <= 0)
                        continue;

                    if (overlay == null)
                        overlay = new byte[bHapticsManager.MaxMotorsPerDotPoint];

                    int index = node - 1;
                    if (overlay[index] < intensity)
                        overlay[index] = ToByteIntensity(intensity);
                }
            }

            return overlay;
        }

        private void ExpirePunchPulses(long nowMs)
        {
            for (int i = PunchPulses.Count - 1; i >= 0; i--)
            {
                if (nowMs - PunchPulses[i].StartMs > MaxPunchLifetimeMs(PunchPulses[i]))
                    PunchPulses.RemoveAt(i);
            }
        }

        private int CalculatePunchNodeIntensity(PunchPulse pulse, int node, long nowMs, bool rippleEnabled)
        {
            int distance = GetVestNodeDistance(pulse.Node, node);
            if (!rippleEnabled && distance != 0)
                return 0;

            if (distance > 2)
                return 0;

            int delayMs = distance * Program.VRChat.punch.Value.RippleDelayMs;
            float elapsedMs = nowMs - pulse.StartMs - delayMs;
            if (elapsedMs < 0f || elapsedMs > pulse.DurationMs)
                return 0;

            float progress = elapsedMs / pulse.DurationMs;
            float envelope = 1f - progress;
            envelope *= envelope;

            float falloff = distance == 0 ? 1f : distance == 1 ? 0.45f : 0.22f;
            return (int)System.Math.Round(pulse.Intensity * falloff * envelope);
        }

        private int MaxPunchLifetimeMs(PunchPulse pulse)
            => pulse.DurationMs + (Program.VRChat.punch.Value.RippleDelayMs * 2) + 100;

        private int GetVestNodeDistance(int leftNode, int rightNode)
        {
            int leftIndex = leftNode - 1;
            int rightIndex = rightNode - 1;
            int leftRow = leftIndex / VestColumns;
            int leftColumn = leftIndex % VestColumns;
            int rightRow = rightIndex / VestColumns;
            int rightColumn = rightIndex % VestColumns;
            return System.Math.Abs(leftRow - rightRow) + System.Math.Abs(leftColumn - rightColumn);
        }

        private int GetPunchIntensity(int band)
        {
            int baseIntensity = band >= PunchBandHard
                ? Program.VRChat.punch.Value.HardIntensity
                : Program.VRChat.punch.Value.LightIntensity;
            float strength = Program.VRChat.punch.Value.Strength / 100f;
            if (PunchStrengthMenuSet && PunchStrengthMenuValue > 0f)
                strength *= PunchStrengthMenuValue;
            return (int)System.Math.Round(baseIntensity * strength);
        }

        private int GetPunchDurationMs(int band)
        {
            int baseDuration = band >= PunchBandHard
                ? Program.VRChat.punch.Value.HardDurationMs
                : Program.VRChat.punch.Value.LightDurationMs;
            float duration = Program.VRChat.punch.Value.Duration / 100f;
            if (PunchDurationMenuSet && PunchDurationMenuValue > 0f)
                duration *= PunchDurationMenuValue;
            return System.Math.Max(1, (int)System.Math.Round(baseDuration * duration));
        }

        private bool IsPunchEnabled()
            => Program.VRChat.punch.Value.Enabled && (!PunchEnabledMenuSet || PunchEnabledMenuValue);

        private bool IsPunchRippleEnabled()
            => Program.VRChat.punch.Value.Ripple && (!PunchRippleMenuSet || PunchRippleMenuValue);

        private static readonly System.Diagnostics.Stopwatch PunchClock = System.Diagnostics.Stopwatch.StartNew();

        private static long NowMs()
            => PunchClock.ElapsedMilliseconds;

        private void SetDeviceNodeIntensity(PositionID PositionID, int node, int intensity)
        {
            if ((Devices.Count <= 0) || !Devices.TryGetValue(PositionID, out Device device))
                return;
            device.SetNodeIntensity(node, intensity);
        }

        private static byte ToByteIntensity(int intensity)
            => (byte)intensity.Clamp(0, 255);

        private class Device
        {
            internal PositionID Position { get; }
            private byte[] Buffer = new byte[bHapticsManager.MaxMotorsPerDotPoint];
            private byte[] SubmitBuffer = new byte[bHapticsManager.MaxMotorsPerDotPoint];

            internal Device(PositionID position)
                => Position = position;

            internal void Submit(byte[] overlay)
            {
                if (!Program.Devices.PositionIDToEnabled(Position))
                    return;

                if (!bHapticsManager.IsDeviceConnected(Position))
                    return;

               /* if (Program.UdonAudioLink.PositionIDToEnabled(Position))
                {
                    switch (Program.UdonAudioLink.udonAudioLink.Value.ReactionMode)
                    {
                        case UdonAudioLinkConfig.UdonAudioLink.UdonAudioLinkModeEnum.FULL:
                            Submit_UdonAudioLink_Full();
                            goto default;

                        default:
                            break;
                    }
                }*/

                byte[] output = Buffer;
                if (overlay != null)
                {
                    for (int i = 0; i < Buffer.Length; i++)
                        SubmitBuffer[i] = Buffer[i] > overlay[i] ? Buffer[i] : overlay[i];
                    output = SubmitBuffer;
                }

                bHapticsManager.Play($"{BuildInfo.Name}_{Position}", 150, Position, output);
            }

            internal int GetNodeIntensity(int node)
                => Buffer[node - 1];

            internal void SetNodeIntensity(int node, int intensity)
                => Buffer[node - 1] = ToByteIntensity(intensity);

            internal void Reset()
            {
                for (int i = 1; i < Buffer.Length + 1; i++)
                    SetNodeIntensity(i, 0);
            }

            /*
            private void Submit_UdonAudioLink_Full()
            {
                if (Program.VRCSupport.UdonAudioLink <= 0)
                    return;

                int audioLinkIntensity = (Program.UdonAudioLink.PositionIDToIntensity(Position) * (Program.VRCSupport.UdonAudioLink / 100));
                for (int i = 0; i < Buffer.Length; i++)
                    if (Program.UdonAudioLink.udonAudioLink.Value.OverrideTouch || (Buffer[i] < audioLinkIntensity))
                        Buffer[i] = (byte)audioLinkIntensity;
            }
            */
        }
    }
}
