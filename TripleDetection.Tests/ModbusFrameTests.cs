using System;
using System.Linq;
using TripleDetection.Infrastructure.Device;
using Xunit;

namespace TripleDetection.Tests
{
    /// <summary>Modbus 帧编解码：请求构造 + 响应解析 + 异常响应。</summary>
    public class ModbusFrameTests
    {
        [Fact]
        public void BuildReadHoldingRequest_ProducesValidMbapFrame()
        {
            var frame = ModbusFrame.BuildReadHoldingRequest(0x12, 1, 0, 16);

            Assert.Equal(12, frame.Length); // MBAP(7) + PDU(5)
            Assert.Equal(0x12, frame[0]);   // 事务号高字节
            Assert.Equal(0x00, frame[1]);   // 事务号低字节
            Assert.Equal(0x00, frame[2]);   // 协议标识 MBAP
            Assert.Equal(0x00, frame[3]);
            Assert.Equal(6, (frame[4] << 8) | frame[5]);      // 长度 = UnitId(1) + PDU(5)
            Assert.Equal(1, frame[6]);                         // UnitId
            Assert.Equal(ModbusFrame.FnReadHolding, frame[7]);
            Assert.Equal(0, (frame[8] << 8) | frame[9]);      // 起始寄存器 0
            Assert.Equal(16, (frame[10] << 8) | frame[11]);   // 数量 16
        }

        [Fact]
        public void ReadResponse_RoundTrip_ReturnsRegisterValues()
        {
            // 构造 0x03 响应：3 个寄存器 [0x0001, 0x1234, 0xFFFF]
            // MBAP: tx=0x12, proto=0, length=9 (UnitId 1 + PDU 8), unitId=1
            var frame = new byte[]
            {
                0x12, 0x00, 0x00, 0x00, 0x00, 0x09, 0x01,
                0x03, 0x06, 0x00, 0x01, 0x12, 0x34, 0xFF, 0xFF
            };

            var values = ModbusFrame.ParseReadResponse(frame, 0x12);

            Assert.Equal(new ushort[] { 0x0001, 0x1234, 0xFFFF }, values);
        }

        [Fact]
        public void ReadResponse_ExceptionResponse_ThrowsWithCode()
        {
            // 0x83 = 0x03 | 0x80，异常码 0x02（非法地址）
            var frame = new byte[] { 0x12, 0x00, 0x00, 0x00, 0x00, 0x03, 0x01, 0x83, 0x02 };

            var ex = Assert.Throws<ModbusException>(() => ModbusFrame.ParseReadResponse(frame, 0x12));
            Assert.Equal(0x02, ex.ExceptionCode);
        }

        [Fact]
        public void ReadResponse_TxIdMismatch_Throws()
        {
            var frame = new byte[] { 0x13, 0x00, 0x00, 0x00, 0x00, 0x05, 0x01, 0x03, 0x02, 0x00, 0x01 };
            Assert.Throws<ModbusException>(() => ModbusFrame.ParseReadResponse(frame, 0x12));
        }

        [Fact]
        public void ReadResponse_TooShort_Throws()
        {
            Assert.Throws<ModbusException>(() => ModbusFrame.ParseReadResponse(new byte[] { 0x12, 0x00 }, 0x12));
        }

        [Fact]
        public void BuildWriteSingleRequest_ProducesEchoableFrame()
        {
            var frame = ModbusFrame.BuildWriteSingleRequest(0x07, 1, 10, 2);

            Assert.Equal(12, frame.Length);
            Assert.Equal(0x07, frame[0]);
            Assert.Equal(ModbusFrame.FnWriteSingle, frame[7]);
            Assert.Equal(10, (frame[8] << 8) | frame[9]);
            Assert.Equal(2, (frame[10] << 8) | frame[11]);

            // 写响应为请求回显
            ModbusFrame.ParseWriteResponse(frame, 0x07, ModbusFrame.FnWriteSingle);
        }

        [Fact]
        public void WriteSingle_ExceptionResponse_Throws()
        {
            var frame = new byte[] { 0x07, 0x00, 0x00, 0x00, 0x00, 0x03, 0x01, 0x86, 0x03 };
            var ex = Assert.Throws<ModbusException>(
                () => ModbusFrame.ParseWriteResponse(frame, 0x07, ModbusFrame.FnWriteSingle));
            Assert.Equal(0x03, ex.ExceptionCode);
        }

        [Fact]
        public void BuildWriteMultipleRequest_EncodesAllValues()
        {
            var values = new ushort[] { 5, 6, 7 };
            var frame = ModbusFrame.BuildWriteMultipleRequest(0x01, 1, 100, values);

            Assert.Equal(7 + 6 + 6, frame.Length); // MBAP + PDU(6) + 3*2
            Assert.Equal(ModbusFrame.FnWriteMultiple, frame[7]);
            Assert.Equal(100, (frame[8] << 8) | frame[9]);
            Assert.Equal(3, (frame[10] << 8) | frame[11]);
            Assert.Equal(6, frame[12]); // 字节数
            Assert.Equal(5, (frame[13] << 8) | frame[14]);
            Assert.Equal(6, (frame[15] << 8) | frame[16]);
            Assert.Equal(7, (frame[17] << 8) | frame[18]);

            // 写多响应：MBAP(7) + fn + start + quantity（5 字节 PDU）
            var echo = new byte[12];
            Array.Copy(frame, 0, echo, 0, 12);
            ModbusFrame.ParseWriteResponse(echo, 0x01, ModbusFrame.FnWriteMultiple);
        }
    }
}
