// Protocol framing adapted from Jason Hunter / Viss; see vendor notices.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace ACInfinityDesktop;

public static class Protocol
{
    public static ushort Crc(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xffff;
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++) crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc;
    }
    public static byte[] Request(byte command, ushort sequence, byte[] payload)
    {
        var frame = new byte[payload.Length + 12];
        frame[0] = 0xa5;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), (ushort)payload.Length);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), sequence);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(6), Crc(frame.AsSpan(0, 6)));
        frame[9] = command;
        payload.CopyTo(frame, 10);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(frame.Length - 2), Crc(frame.AsSpan(8, payload.Length + 2)));
        return frame;
    }
    public static byte[] Response(byte[] frame, ushort sequence, byte command)
    {
        if (frame.Length < 12 || frame[0] != 0xa5 || (frame[1] != 0x10 && frame[1] != 0x13)) throw new InvalidDataException("Unknown response header.");
        int n = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(2));
        if (frame.Length != n + 12) throw new InvalidDataException("Response length mismatch.");
        if (BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(6)) != Crc(frame.AsSpan(0, 6)) || BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(n + 10)) != Crc(frame.AsSpan(8, n + 2))) throw new InvalidDataException("Response checksum mismatch.");
        if (BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(4)) != sequence || frame[9] != command) throw new InvalidDataException("Unrelated response.");
        return frame.AsSpan(10, n).ToArray();
    }
    public static Dictionary<byte, byte[]> Fields(byte[] payload)
    {
        var result = new Dictionary<byte, byte[]>();
        for (int i = 0; i < payload.Length;)
        {
            if (i + 2 > payload.Length) throw new InvalidDataException("Truncated parameter.");
            byte key = payload[i++]; int count = payload[i++];
            if (i + count > payload.Length) throw new InvalidDataException("Truncated value.");
            result[key] = payload.AsSpan(i, count).ToArray(); i += count;
        }
        return result;
    }
    public static byte[] Speed(int level)
    {
        if (level < 1 || level > 10) throw new ArgumentOutOfRangeException(nameof(level));
        return new byte[] { 0x10, 1, 2, 0x12, 1, (byte)level };
    }
    public static void ValidateAck(byte[] payload, params byte[] required)
    {
        if (payload.Length == 0 || payload.Length % 2 != 0) throw new InvalidDataException("Invalid settings acknowledgment.");
        var accepted = new HashSet<byte>();
        for (int i = 0; i < payload.Length; i += 2)
        {
            if (payload[i + 1] != 0) throw new InvalidDataException("Controller rejected a setting.");
            accepted.Add(payload[i]);
        }
        foreach (byte id in required) if (!accepted.Contains(id)) throw new InvalidDataException("Incomplete settings acknowledgment.");
    }
}

public sealed class ControllerState
{
    public double? TemperatureC { get; set; }
    public double? Humidity { get; set; }
    public int? Output { get; set; }
    public int Mode { get; set; }
    public int On { get; set; }
    public int Off { get; set; }
    public DateTimeOffset Updated { get; set; }
    public Dictionary<byte, byte[]> Settings { get; set; } = new();
    public string ModeName => Mode switch { 1 => "OFF", 2 => "ON · MANUAL", 3 => "AUTO", 4 => "TIMER TO ON", 5 => "TIMER TO OFF", 6 => "CYCLE", 7 => "SCHEDULE", _ => "Unknown mode" };
    public void ApplyLive(Dictionary<byte, byte[]> fields)
    {
        if (!fields.TryGetValue(0x10, out var mode) || mode.Length != 1 || !fields.TryGetValue(2, out var climate) || climate.Length != 5 || !fields.TryGetValue(3, out var fan) || fan.Length != 5) throw new InvalidDataException("Unexpected Controller 67 live-data layout.");
        Mode = mode[0];
        short t = BinaryPrimitives.ReadInt16BigEndian(climate.AsSpan(1));
        int h = BinaryPrimitives.ReadUInt16BigEndian(climate.AsSpan(3));
        TemperatureC = t != short.MinValue && t >= -4000 && t <= 10000 ? t / 100d : null;
        Humidity = h <= 10000 ? h / 100d : null;
        int output = fan[1] & 15; Output = output <= 10 ? output : null;
        Updated = DateTimeOffset.Now;
    }
}
