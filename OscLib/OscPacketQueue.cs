using OscLib.Utils;
using Rug.Osc;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace OscLib
{
    public class OscPacketQueue : ThreadedTask
    {
        public ConcurrentQueue<OscPacket> PacketQueue = new ConcurrentQueue<OscPacket>();
        private bool ShouldRun;

        // Dropped packets are reported at most once per interval, with a count, so a flood
        // cannot bury the console. Anything on the machine can send one to the port.
        private static readonly TimeSpan DropReportInterval = TimeSpan.FromSeconds(5);
        private DateTime lastDropReport = DateTime.MinValue;
        private int droppedSinceReport;

        public override bool BeginInitInternal()
        {
            if (ShouldRun)
                EndInit();

            ShouldRun = true;
            return true;
        }

        public override bool EndInitInternal()
        {
            ShouldRun = false;
            while (IsAlive()) { Thread.Sleep(1); }
            return true;
        }

        public override void WithinThread()
        {
            while (ShouldRun)
            {
                while (PacketQueue.TryDequeue(out OscPacket packet))
                {
                    try
                    {
                        Handle(packet);
                    }
                    catch (ThreadAbortException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // Rug.Osc accepts more than it can use: it does not validate the
                        // address when it reads a message, only when the address book
                        // looks it up, and a handler can reject arguments it was never
                        // meant to see. That is the packet's fault, not the worker's - and
                        // a dead worker leaves the receiver filling a queue nothing drains.
                        Drop(packet, ex.ToString());
                    }
                }

                if (ShouldRun)
                    Thread.Sleep(1);
            }
        }

        private void Handle(OscPacket packet)
        {
            OscPacketInvokeAction action = OscManager.ShouldInvoke(packet);

            // Rug.Osc hands over datagrams it could not parse as packets flagged HasError.
            // Drop those before anything forwards or invokes them; this used to throw,
            // which took the whole process down with it.
            if (action == OscPacketInvokeAction.HasError)
            {
                Drop(packet, $"{packet.Error}: {packet.ErrorMessage}");
                return;
            }

            if (OscManager.Connection.sender.Value.PipeAllPackets)
                OscManager.oscSender.Send(packet);

            switch (action)
            {
                case OscPacketInvokeAction.Pospone:
                case OscPacketInvokeAction.Invoke:

                    if (!OscManager.Connection.sender.Value.PipeAllPackets)
                        OscManager.oscSender.Send(packet);

                    OscManager.Invoke(packet);

                    goto default;
                case OscPacketInvokeAction.DontInvoke:
                default:
                    break;
            }
        }

        private void Drop(OscPacket packet, string reason)
        {
            droppedSinceReport++;

            DateTime now = DateTime.UtcNow;
            if (now - lastDropReport < DropReportInterval)
                return;

            Console.WriteLine($"Dropped {droppedSinceReport} OSC packet(s)  |  Last from {packet.Origin}  |  {reason}");
            lastDropReport = now;
            droppedSinceReport = 0;
        }
    }
}
