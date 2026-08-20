using System.Runtime.InteropServices;
using Prosody.Native;

namespace Prosody.Infrastructure;

/// <summary>Passes payload bytes through BoltFFI without a signed-byte copy.</summary>
/// <remarks>BoltFFI exposes signed bytes, but the native ABI accepts the original byte array.</remarks>
internal static class NativeSend
{
    internal static Task Send(
        Native.ProsodyClient client,
        string topic,
        string key,
        EventMetadata metadata,
        byte[] payload,
        Dictionary<string, string> carrier,
        CancellationToken cancellationToken
    )
    {
        var topicWriter = new WireWriter();
        topicWriter.WriteString(topic);
        var topicBytes = topicWriter.ToArray();

        var keyWriter = new WireWriter();
        keyWriter.WriteString(key);
        var keyBytes = keyWriter.ToArray();

        var metadataWriter = new WireWriter();
        metadata.Encode(metadataWriter);
        var metadataBytes = metadataWriter.ToArray();

        var carrierWriter = new WireWriter();
        carrierWriter.WriteU32(checked((uint)carrier.Count));
        foreach (var entry in carrier)
        {
            carrierWriter.WriteString(entry.Key);
            carrierWriter.WriteString(entry.Value);
        }

        var carrierBytes = carrierWriter.ToArray();
        return BoltFFIAsync.CallAsyncVoid(
            () =>
                NativeProsodyClientSend(
                    client.Handle,
                    topicBytes,
                    (nuint)topicBytes.Length,
                    keyBytes,
                    (nuint)keyBytes.Length,
                    metadataBytes,
                    (nuint)metadataBytes.Length,
                    payload,
                    (nuint)payload.Length,
                    carrierBytes,
                    (nuint)carrierBytes.Length
                ),
            NativeMethods.NativeProsodyClientSendPoll,
            future => Complete(future, cancellationToken),
            NativeMethods.NativeProsodyClientSendCancel,
            NativeMethods.NativeProsodyClientSendFree,
            cancellationToken
        );
    }

    private static void Complete(nint future, CancellationToken cancellationToken)
    {
        var errorBuffer = NativeMethods.NativeProsodyClientSendComplete(future, out var status);
        BoltFFIAsync.ThrowIfStatus(status, cancellationToken);
        if (errorBuffer.ptr == 0)
        {
            return;
        }

        try
        {
            throw new FfiErrorException(FfiError.Decode(new WireReader(errorBuffer)));
        }
        finally
        {
            NativeMethods.FreeBuf(errorBuffer);
        }
    }

    [DllImport(NativeMethods.LibName, EntryPoint = "boltffi_method_class_prosody_ffi_client_prosody_client_send")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern nint NativeProsodyClientSend(
        ulong receiver,
        [In] byte[] topicBytes,
        nuint topicLength,
        [In] byte[] keyBytes,
        nuint keyLength,
        [In] byte[] metadataBytes,
        nuint metadataLength,
        [In] byte[] payload,
        nuint payloadLength,
        [In] byte[] carrierBytes,
        nuint carrierLength
    );
}
