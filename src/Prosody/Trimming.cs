namespace Prosody;

/// <summary>
/// Messages for <c>RequiresUnreferencedCode</c> and <c>RequiresDynamicCode</c>. Each names the
/// reflection path and the trim-safe alternative once.
/// </summary>
internal static class Trimming
{
    internal const string JsonResolver =
        "Auto-installs DefaultJsonTypeInfoResolver when no TypeInfoResolver is set via ConfigureJsonOptions. Configure a source-generated JsonSerializerContext for trimming and native AOT.";

    internal const string OptionsBinding =
        "Binds ClientOptions from IConfiguration and auto-installs DefaultJsonTypeInfoResolver. Configure a source-generated JsonSerializerContext via ClientOptions.ConfigureJsonOptions for trimming and native AOT.";

    internal const string JsonMetadata =
        "Resolves JSON metadata at run time. Use the overload that accepts JsonTypeInfo values.";

    internal const string HandlerReflection =
        "Reads PermanentErrorAttribute from handler methods via reflection and needs their interface map at run time. Pass an IPermanentErrorClassifier to avoid this.";
}
