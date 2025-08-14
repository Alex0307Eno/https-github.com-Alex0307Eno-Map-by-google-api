namespace Map.Models
{
    public class GoogleMapsSettings
    {
        public string ApiKey { get; set; } = "";
        public GoogleCloudSettings GoogleCloud { get; set; } = new();
    }

    public class GoogleCloudSettings
    {
        public string ProjectId { get; set; } = "";
        public string? ProjectNumber { get; set; }
        public string? CredentialsPath { get; set; }
        public Dictionary<string, int> Quota { get; set; } = new();
    }
}
