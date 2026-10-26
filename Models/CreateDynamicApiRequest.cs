namespace MockServer.Models
{
    public class CreateDynamicApiRequest
    {
        public string ApiName { get; set; } = string.Empty; // e.g., "Orders"
        public string Route { get; set; } = string.Empty;   // e.g., "api/orders/calculate"
        public string HttpMethod { get; set; } = "POST";    // GET, POST, PUT, DELETE

        // Sent as JSON strings because of multipart/form-data form fields
        public string ResponsePropertiesJson { get; set; } = "{}";

        // JSON array or object defining input parameters/filters
        public string RequestParametersJson { get; set; } = "[]";

        // Response structure: "PaginatedList", "SingleObject", "DirectArray"
        public string ResponseFormat { get; set; } = "PaginatedList";

        // The uploaded Excel file (.xlsx)
        public IFormFile ExcelFile { get; set; } = null!;
    }
}
