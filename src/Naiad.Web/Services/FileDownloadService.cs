public class FileDownloadService(IJSRuntime jsRuntime)
{
    /// <summary>Triggers a browser download of arbitrary bytes — works for both text and binary formats.</summary>
    public async Task DownloadAsync(string fileName, string contentType, byte[] bytes)
    {
        var base64 = Convert.ToBase64String(bytes);
        await jsRuntime.InvokeVoidAsync("fileDownload.downloadBlob", fileName, contentType, base64);
    }

    /// <summary>Triggers a browser download of UTF-8 text (e.g. the rendered SVG markup).</summary>
    public Task DownloadTextAsync(string fileName, string contentType, string text) =>
        DownloadAsync(fileName, contentType, Encoding.UTF8.GetBytes(text));
}
