using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Padel.Api.Services;

public sealed record MercadoPagoSavedCardResult(
    string CustomerId,
    string CardId,
    string PaymentMethodId,
    string? CardBrand,
    string? LastFourDigits);

public interface IMercadoPagoCustomerCardService
{
    Task<MercadoPagoSavedCardResult> SaveCardAsync(
        string email,
        string? existingCustomerId,
        string cardToken,
        string accessToken,
        CancellationToken cancellationToken);

    Task DeleteCardAsync(
        string customerId,
        string cardId,
        string accessToken,
        CancellationToken cancellationToken);
}

public sealed class MercadoPagoCustomerCardService(IHttpClientFactory httpClientFactory) : IMercadoPagoCustomerCardService
{
    public async Task<MercadoPagoSavedCardResult> SaveCardAsync(
        string email,
        string? existingCustomerId,
        string cardToken,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var httpClient = httpClientFactory.CreateClient();
        var customerId = await ResolveCustomerIdAsync(httpClient, email, existingCustomerId, accessToken, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.mercadopago.com/v1/customers/{customerId}/cards")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { token = cardToken }), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Mercado Pago rechazo guardar la tarjeta ({(int)response.StatusCode}). {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        var cardId = root.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Mercado Pago no devolvio el identificador de la tarjeta.");
        var lastFourDigits = root.TryGetProperty("last_four_digits", out var lastFourProperty)
            ? lastFourProperty.GetString()
            : null;
        var paymentMethodId = root.TryGetProperty("payment_method", out var paymentMethodProperty) &&
            paymentMethodProperty.TryGetProperty("id", out var paymentMethodIdProperty)
                ? paymentMethodIdProperty.GetString()
                : null;
        var cardBrand = root.TryGetProperty("payment_method", out var brandProperty) &&
            brandProperty.TryGetProperty("name", out var brandNameProperty)
                ? brandNameProperty.GetString()
                : paymentMethodId;

        return new MercadoPagoSavedCardResult(
            customerId,
            cardId,
            paymentMethodId ?? "card",
            cardBrand,
            lastFourDigits);
    }

    public async Task DeleteCardAsync(
        string customerId,
        string cardId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var httpClient = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"https://api.mercadopago.com/v1/customers/{customerId}/cards/{cardId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Mercado Pago rechazo eliminar la tarjeta ({(int)response.StatusCode}). {responseBody}");
    }

    private static async Task<string> ResolveCustomerIdAsync(
        HttpClient httpClient,
        string email,
        string? existingCustomerId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(existingCustomerId))
        {
            using var existingCustomerRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.mercadopago.com/v1/customers/{existingCustomerId}");
            existingCustomerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var existingCustomerResponse = await httpClient.SendAsync(existingCustomerRequest, cancellationToken);
            if (existingCustomerResponse.IsSuccessStatusCode)
            {
                return existingCustomerId;
            }
        }

        using var searchRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.mercadopago.com/v1/customers/search?email={Uri.EscapeDataString(email)}");
        searchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var searchResponse = await httpClient.SendAsync(searchRequest, cancellationToken);
        var searchBody = await searchResponse.Content.ReadAsStringAsync(cancellationToken);
        if (searchResponse.IsSuccessStatusCode)
        {
            using var searchDocument = JsonDocument.Parse(searchBody);
            if (searchDocument.RootElement.TryGetProperty("results", out var results) &&
                results.ValueKind == JsonValueKind.Array)
            {
                foreach (var result in results.EnumerateArray())
                {
                    if (result.TryGetProperty("id", out var idProperty))
                    {
                        var customerId = idProperty.GetString();
                        if (!string.IsNullOrWhiteSpace(customerId))
                        {
                            return customerId;
                        }
                    }
                }
            }
        }

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.mercadopago.com/v1/customers")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { email }), Encoding.UTF8, "application/json")
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var createResponse = await httpClient.SendAsync(createRequest, cancellationToken);
        var createBody = await createResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!createResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Mercado Pago rechazo crear el cliente ({(int)createResponse.StatusCode}). {createBody}");
        }

        using var createDocument = JsonDocument.Parse(createBody);
        return createDocument.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Mercado Pago no devolvio el identificador del cliente.");
    }
}
