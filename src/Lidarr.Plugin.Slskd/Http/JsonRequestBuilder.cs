namespace NzbDrone.Common.Http;

public class JsonRequestBuilder : HttpRequestBuilder
{
    private string _jsonData = string.Empty;

    public JsonRequestBuilder(string baseUrl)
        : base(baseUrl)
    {
    }

    public JsonRequestBuilder(bool useHttps, string host, int port, string urlBase = null)
        : base(useHttps, host, port, urlBase)
    {
    }

    public void SetJsonData(string jsonData)
    {
        _jsonData = jsonData;
    }

    protected override void Apply(HttpRequest request)
    {
        base.Apply(request);

        if (!string.IsNullOrEmpty(_jsonData))
        {
            request.Headers.ContentType = "application/json";

            // TODO: Sucks but im sleepy rn
            request.SetContent(_jsonData);
        }
    }

    public override JsonRequestBuilder Resource(string resourceUrl)
    {
        base.Resource(resourceUrl);
        return this;
    }

    public override JsonRequestBuilder AddQueryParam(string key, object value, bool replace = false)
    {
        base.AddQueryParam(key, value, replace);
        return this;
    }

    public override JsonRequestBuilder Post()
    {
        base.Post();
        return this;
    }

    public override JsonRequestBuilder SetHeader(string name, string value)
    {
        base.SetHeader(name, value);
        return this;
    }
}
