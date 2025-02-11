namespace OrderService
{
    public class CatalogServiceClient
    {
        private readonly HttpClient _httpClient;

    public CatalogServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<bool> CheckBookExists(int bookId)
    {
        var response = await _httpClient.GetAsync($"http://localhost:5243/api/books/{bookId}");
        return response.IsSuccessStatusCode;
    }

    }
}
