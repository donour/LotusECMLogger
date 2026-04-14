namespace LotusECMLogger.Controls
{
    internal sealed class EditableRequestRow
    {
        public string Type { get; set; } = "Mode22";
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string PidsText { get; set; } = string.Empty;
        public string PidHighText { get; set; } = string.Empty;
        public string PidLowText { get; set; } = string.Empty;

        public static EditableRequestRow FromRequest(OBDRequestJson request)
        {
            return new EditableRequestRow
            {
                Type = string.IsNullOrWhiteSpace(request.Type) ? "Mode22" : request.Type,
                Name = request.Name,
                Description = request.Description,
                Category = request.Category,
                Unit = request.Unit,
                PidsText = request.Pids == null ? string.Empty : string.Join(", ", request.Pids.Select(b => $"0x{b:X2}")),
                PidHighText = request.PidHigh.HasValue ? $"0x{request.PidHigh.Value:X2}" : string.Empty,
                PidLowText = request.PidLow.HasValue ? $"0x{request.PidLow.Value:X2}" : string.Empty
            };
        }
    }
}
