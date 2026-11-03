using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace TruckGeodis;

public sealed class SharePointTruckRequestService
{
    private const string ListTitle = "GeoTruckRequests";
    private const string LibraryTitle = "Shared Documents";
    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly Uri _siteUri;
    private readonly string _sitePath;
    private readonly string _listTitle;
    private readonly string _libraryTitle;

    public SharePointTruckRequestService(
        HttpClient httpClient,
        TokenCredential credential,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _credential = credential;
        _siteUri = new Uri(
            RequiredSetting(configuration, "SharePointSiteUrl").TrimEnd('/') + "/",
            UriKind.Absolute);
        _sitePath = _siteUri.AbsolutePath.TrimEnd('/');
        _listTitle = configuration["SharePointListTitle"] ?? ListTitle;
        _libraryTitle = configuration["SharePointLibraryTitle"] ?? LibraryTitle;

        if (_siteUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("SharePointSiteUrl must use HTTPS.");
        }
    }

    public async Task<SharePointSubmissionResult> CreateRequestAndUploadPhotosAsync(
        string trailerNo,
        string carrier,
        string pickupLocation,
        string existingDamage,
        string damaged,
        string description,
        IFormFile? frontPhoto,
        IFormFile? driverSidePhoto,
        CancellationToken cancellationToken)
    {
        var entityType = await GetListItemEntityTypeAsync(cancellationToken);
        var itemId = await CreateListItemAsync(
            entityType,
            trailerNo,
            carrier,
            pickupLocation,
            existingDamage,
            damaged,
            description,
            cancellationToken);

        var uniqueId = await GetItemUniqueIdAsync(itemId, cancellationToken);
        var folderName = uniqueId.ToString("D");
        var folderServerRelativeUrl = $"{_sitePath}/{_libraryTitle}/{folderName}";
        await CreateFolderAsync(folderServerRelativeUrl, cancellationToken);

        var photoUrls = new Dictionary<string, string>(StringComparer.Ordinal);
        var photoFields = new Dictionary<string, (IFormFile? File, string FieldName, string DisplayName)>
        {
            ["Front_x0020_Photo"] = (frontPhoto, "frontPhoto", "Front Photo"),
            ["Driver_x0020_Side_x0020_Photo"] = (driverSidePhoto, "driverSidePhoto", "Driver Side Photo")
        };

        foreach (var (sharePointField, photo) in photoFields)
        {
            if (photo.File is null || photo.File.Length == 0)
            {
                continue;
            }

            var fileName = CreateSafeFileName(photo.FieldName, photo.File.FileName);
            var serverRelativeUrl = await UploadFileAsync(
                folderServerRelativeUrl,
                fileName,
                photo.File,
                cancellationToken);
            photoUrls[sharePointField] = new UriBuilder(_siteUri.Scheme, _siteUri.Host)
            {
                Path = serverRelativeUrl
            }.Uri.AbsoluteUri;
        }

        if (photoUrls.Count > 0)
        {
            await UpdatePhotoLinksAsync(
                itemId,
                entityType,
                photoUrls,
                photoFields,
                cancellationToken);
        }

        return new SharePointSubmissionResult(itemId, uniqueId, photoUrls);
    }

    private async Task<string> GetListItemEntityTypeAsync(CancellationToken cancellationToken)
    {
        var listTitle = EscapeODataString(_listTitle);
        using var response = await SendAsync(
            HttpMethod.Get,
            $"_api/web/lists/GetByTitle('{listTitle}')?$select=ListItemEntityTypeFullName",
            cancellationToken: cancellationToken);
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        return GetStringProperty(json.RootElement, "ListItemEntityTypeFullName")
            ?? throw new InvalidOperationException(
                "SharePoint did not return the list item entity type for GeoTruckRequests.");
    }

    private async Task<int> CreateListItemAsync(
        string entityType,
        string trailerNo,
        string carrier,
        string pickupLocation,
        string existingDamage,
        string damaged,
        string description,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, object?>
        {
            ["__metadata"] = new { type = entityType },
            ["Title"] = trailerNo,
            ["TrailerNo"] = trailerNo,
            ["Carrier"] = carrier,
            ["Pickup_x0020_location"] = pickupLocation,
            ["Do_x0020_you_x0020_see_x0020_any"] = existingDamage,
            ["What_x0020_is_x0020_damaged_x003"] = damaged,
            ["Description"] = description
        };

        using var content = JsonContent(fields);
        using var response = await SendAsync(
            HttpMethod.Post,
            $"_api/web/lists/GetByTitle('{EscapeODataString(_listTitle)}')/items",
            content,
            cancellationToken);
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        var id = GetIntProperty(json.RootElement, "Id");
        if (id <= 0)
        {
            throw new InvalidOperationException(
                "SharePoint created the list item but did not return its item ID.");
        }

        return id;
    }

    private async Task<Guid> GetItemUniqueIdAsync(int itemId, CancellationToken cancellationToken)
    {
        var listTitle = EscapeODataString(_listTitle);
        using var response = await SendAsync(
            HttpMethod.Get,
            $"_api/web/lists/GetByTitle('{listTitle}')/items({itemId})?$select=Id,UniqueId",
            cancellationToken: cancellationToken);
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        var uniqueId = GetStringProperty(json.RootElement, "UniqueId");
        if (!Guid.TryParse(uniqueId, out var parsedUniqueId))
        {
            throw new InvalidOperationException(
                $"SharePoint did not return a valid UniqueId for list item {itemId}.");
        }

        return parsedUniqueId;
    }

    private async Task CreateFolderAsync(
        string serverRelativeUrl,
        CancellationToken cancellationToken)
    {
        using var content = JsonContent(new { ServerRelativeUrl = serverRelativeUrl });
        using var response = await SendAsync(
            HttpMethod.Post,
            "_api/web/folders",
            content,
            cancellationToken);
    }

    private async Task<string> UploadFileAsync(
        string folderServerRelativeUrl,
        string fileName,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var encodedFolderPath = string.Join(
            "/",
            folderServerRelativeUrl.Split('/').Select(Uri.EscapeDataString));
        var encodedFileName = Uri.EscapeDataString(fileName);
        var requestPath =
            $"_api/web/GetFolderByServerRelativeUrl('{encodedFolderPath}')/Files/add(url='{encodedFileName}',overwrite=true)";

        using var stream = file.OpenReadStream();
        using var content = new StreamContent(stream);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(
            string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType);

        using var response = await SendAsync(
            HttpMethod.Post,
            requestPath,
            content,
            cancellationToken);
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        return GetStringProperty(json.RootElement, "ServerRelativeUrl")
            ?? throw new InvalidOperationException(
                $"SharePoint uploaded {fileName} but did not return its file URL.");
    }

    private async Task UpdatePhotoLinksAsync(
        int itemId,
        string entityType,
        IReadOnlyDictionary<string, string> photoUrls,
        IReadOnlyDictionary<string, (IFormFile? File, string FieldName, string DisplayName)> photoFields,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, object?>
        {
            ["__metadata"] = new { type = entityType }
        };

        foreach (var (fieldName, url) in photoUrls)
        {
            fields[fieldName] = new
            {
                __metadata = new { type = "SP.FieldUrlValue" },
                Url = url,
                Description = photoFields[fieldName].DisplayName
            };
        }

        using var content = JsonContent(fields);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"_api/web/lists/GetByTitle('{EscapeODataString(_listTitle)}')/items({itemId})")
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("X-HTTP-Method", "MERGE");
        request.Headers.TryAddWithoutValidation("IF-MATCH", "*");

        using var response = await SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, new Uri(_siteUri, path))
        {
            Content = content
        };
        return await SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(
                new[] { $"{_siteUri.Scheme}://{_siteUri.Host}/.default" }),
            cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
            {
                Parameters = { new NameValueHeaderValue("odata", "nometadata") }
            });

        var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var statusCode = response.StatusCode;
        response.Dispose();
        throw new HttpRequestException(
            $"SharePoint request failed with {(int)statusCode} ({statusCode}): {responseBody}",
            inner: null,
            statusCode);
    }

    private static StringContent JsonContent(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json;odata=nometadata");

    private static string RequiredSetting(IConfiguration configuration, string name) =>
        configuration[name] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Missing required application setting '{name}'.");

    private static string EscapeODataString(string value) => value.Replace("'", "''");

    private static string CreateSafeFileName(string fieldName, string originalFileName)
    {
        var extension = Path.GetExtension(Path.GetFileName(originalFileName));
        if (!Regex.IsMatch(extension, @"^\.[A-Za-z0-9]{1,10}$"))
        {
            extension = string.Empty;
        }

        return $"{fieldName}{extension.ToLowerInvariant()}";
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        var value = GetProperty(element, propertyName);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static int GetIntProperty(JsonElement element, string propertyName)
    {
        var value = GetProperty(element, propertyName);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : 0;
    }

    private static JsonElement GetProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            return value;
        }

        if (element.TryGetProperty("d", out var wrapped)
            && wrapped.TryGetProperty(propertyName, out value))
        {
            return value;
        }

        return default;
    }
}

public sealed record SharePointSubmissionResult(
    int ItemId,
    Guid UniqueId,
    IReadOnlyDictionary<string, string> PhotoUrls);
