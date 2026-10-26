namespace MockServer.Models
{
    public class RequestParameterDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "string"; // int, string, decimal, bool, datetime, double, long
        public string? TargetProperty { get; set; }  // Target Excel / response column name
        public string MatchMode { get; set; } = "Equals"; // Equals, Contains, GreaterThan, LessThan, GreaterThanOrEqual, LessThanOrEqual
        public bool IsRequired { get; set; } = false;
    }
}
