using System;
using System.Collections.Generic;

namespace TripleDetection.Infrastructure.Device
{
    /// <summary>Modbus 协议错误（异常响应或帧格式错误）</summary>
    public class ModbusException : Exception
    {
        public byte ExceptionCode { get; }
        public ModbusException(string message, byte exceptionCode = 0) : base(message)
        {
            ExceptionCode = exceptionCode;
        }
    }

    /// <summary>
    /// Modbus TCP 帧编解码（MBAP 头 + PDU），纯静态、无状态、可单测。
    /// 支持：0x03 读保持寄存器、0x06 写单个寄存器、0x10 写多个寄存器。
    /// </summary>
    public static class ModbusFrame
    {
        public const byte FnReadHolding = 0x03;
        public const byte FnWriteSingle = 0x06;
        public const byte FnWriteMultiple = 0x10;

        /// <summary>构造 0x03 读保持寄存器请求帧</summary>
        public static byte[] BuildReadHoldingRequest(byte transactionId, byte unitId, int startRegister, int quantity)
        {
            var pdu = new byte[5];
            pdu[0] = FnReadHolding;
            WriteInt16(pdu, 1, startRegister);
            WriteInt16(pdu, 3, quantity);
            return WrapMbap(transactionId, unitId, pdu);
        }

        /// <summary>构造 0x06 写单个寄存器请求帧</summary>
        public static byte[] BuildWriteSingleRequest(byte transactionId, byte unitId, int register, ushort value)
        {
            var pdu = new byte[5];
            pdu[0] = FnWriteSingle;
            WriteInt16(pdu, 1, register);
            WriteInt16(pdu, 3, value);
            return WrapMbap(transactionId, unitId, pdu);
        }

        /// <summary>构造 0x10 写多个寄存器请求帧</summary>
        public static byte[] BuildWriteMultipleRequest(byte transactionId, byte unitId, int startRegister, ushort[] values)
        {
            var pdu = new byte[6 + values.Length * 2];
            pdu[0] = FnWriteMultiple;
            WriteInt16(pdu, 1, startRegister);
            WriteInt16(pdu, 3, values.Length);
            pdu[5] = (byte)(values.Length * 2);
            for (int i = 0; i < values.Length; i++)
                WriteInt16(pdu, 6 + i * 2, values[i]);
            return WrapMbap(transactionId, unitId, pdu);
        }

        /// <summary>
        /// 解析 0x03 读响应帧，返回寄存器值数组。
        /// 帧布局：MBAP 头（7 字节：事务号 2 + 协议标识 2 + 长度 2 + 单元 ID 1）+ PDU。
        /// 校验事务号、功能码与字节数；异常响应抛出 ModbusException。
        /// </summary>
        public static ushort[] ParseReadResponse(byte[] frame, byte expectedTransactionId)
        {
            EnsureFrame(frame, 8);
            if (frame[0] != expectedTransactionId)
                throw new ModbusException($"事务号不匹配：期望 {expectedTransactionId}，实际 {frame[0]}");
            byte fn = frame[7];
            if (fn == (FnReadHolding | 0x80))
                throw new ModbusException("PLC 返回读异常响应", frame[8]);
            if (fn != FnReadHolding)
                throw new ModbusException($"功能码不匹配：期望 {FnReadHolding:X2}，实际 {fn:X2}");

            int byteCount = frame[8];
            EnsureFrame(frame, 9 + byteCount);
            if (byteCount % 2 != 0)
                throw new ModbusException($"字节数非法：{byteCount}");

            var values = new ushort[byteCount / 2];
            for (int i = 0; i < values.Length; i++)
                values[i] = ReadInt16(frame, 9 + i * 2);
            return values;
        }

        /// <summary>
        /// 解析 0x06/0x10 写响应帧（请求回显）。
        /// 异常响应抛出 ModbusException。
        /// </summary>
        public static void ParseWriteResponse(byte[] frame, byte expectedTransactionId, byte expectedFunction)
        {
            EnsureFrame(frame, 9);
            if (frame[0] != expectedTransactionId)
                throw new ModbusException($"事务号不匹配：期望 {expectedTransactionId}，实际 {frame[0]}");
            byte fn = frame[7];
            if (fn == (expectedFunction | 0x80))
                throw new ModbusException("PLC 返回写异常响应", frame[8]);
            if (fn != expectedFunction)
                throw new ModbusException($"功能码不匹配：期望 {expectedFunction:X2}，实际 {fn:X2}");
        }

        /// <summary>标准 MBAP 头（7 字节）+ PDU；长度字段 = 单元 ID(1) + PDU 长度</summary>
        private static byte[] WrapMbap(byte transactionId, byte unitId, byte[] pdu)
        {
            var frame = new byte[7 + pdu.Length];
            frame[0] = transactionId;
            frame[1] = 0;
            frame[2] = 0; // 协议标识 MBAP
            frame[3] = 0;
            WriteInt16(frame, 4, 1 + pdu.Length);
            frame[6] = unitId;
            Array.Copy(pdu, 0, frame, 7, pdu.Length);
            return frame;
        }

        private static void EnsureFrame(byte[] frame, int minLength)
        {
            if (frame == null || frame.Length < minLength)
                throw new ModbusException($"帧长度不足：需要 {minLength}，实际 {frame?.Length ?? 0}");
        }

        private static void WriteInt16(byte[] buf, int offset, int value)
        {
            buf[offset] = (byte)((value >> 8) & 0xFF);
            buf[offset + 1] = (byte)(value & 0xFF);
        }

        private static ushort ReadInt16(byte[] buf, int offset)
        {
            return (ushort)((buf[offset] << 8) | buf[offset + 1]);
        }
    }
}
