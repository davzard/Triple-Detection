namespace TripleDetection.Infrastructure.Device
{
    /// <summary>
    /// PLC 保持寄存器映射（基地址 40001，偏移 0 起）。
    /// 默认值来自设计文档 §3.1 寄存器表，可被 PlcRegisterMap.json 覆盖。
    /// </summary>
    public class PlcRegisterMap
    {
        // PLC → App
        public int TriggerOffset { get; set; } = 0;
        public int RecipeChangeOffset { get; set; } = 1;
        public int RecipeIdOffset { get; set; } = 2;
        public int LineSpeedOffset { get; set; } = 3;

        // App → PLC
        public int ResultOffset { get; set; } = 10;
        public int ResultValidOffset { get; set; } = 11;
        public int RejectCmdOffset { get; set; } = 12;
        public int LineStopOffset { get; set; } = 13;
        public int AlarmCodeOffset { get; set; } = 14;
        public int StationStatusOffset { get; set; } = 15;

        // 可选：剔除参数寄存器（-1 表示未配置，PLC 侧使用固定参数）
        public int RejectDelayOffset { get; set; } = -1;
        public int RejectDurationOffset { get; set; } = -1;

        /// <summary>加载 JSON 覆盖配置；文件不存在或解析失败时返回默认映射</summary>
        public static PlcRegisterMap Load(string jsonPath)
        {
            var map = new PlcRegisterMap();
            try
            {
                if (System.IO.File.Exists(jsonPath))
                {
                    var json = System.IO.File.ReadAllText(jsonPath);
                    var loaded = System.Text.Json.JsonSerializer.Deserialize<PlcRegisterMap>(json);
                    if (loaded != null) map = loaded;
                }
            }
            catch
            {
                // 配置损坏时回退默认映射，不阻塞产线启动
            }
            return map;
        }
    }
}
