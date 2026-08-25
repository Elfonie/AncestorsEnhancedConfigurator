using System.Runtime.InteropServices;
using System.Text;

namespace AncestorsEnhanced.App.Discord;

internal sealed record DiscordRichPresenceActivity(
    string Details,
    string LargeImage,
    string LargeText,
    string SmallImage,
    string SmallText);

internal interface IDiscordRichPresenceNative : IDisposable
{
    void Start(DiscordRichPresenceActivity activity);

    void RunCallbacks();
}

internal sealed class DiscordRichPresenceService : IDisposable
{
    internal const ulong ApplicationId = 1540596294521856002;

    private static readonly DiscordRichPresenceActivity Activity = new(
        "Open",
        "big_logo",
        "Ancestors Enhanced Configurator",
        "small_logo",
        "Ancestors Enhanced Configurator");

    private readonly IDiscordRichPresenceNative _native;
    private bool _disabled;
    private bool _disposed;
    private bool _started;

    public DiscordRichPresenceService()
        : this(new DiscordSocialSdkNative(ApplicationId))
    {
    }

    internal DiscordRichPresenceService(IDiscordRichPresenceNative native)
    {
        _native = native;
    }

    public void Start()
    {
        if (_disposed || _disabled || _started)
        {
            return;
        }

        try
        {
            _native.Start(Activity);
            _started = true;
        }
        catch (Exception exception) when (IsOptionalIntegrationFailure(exception))
        {
            _disabled = true;
            AppDiagnostics.Logger?.Write($"Discord Rich Presence is unavailable: {exception.GetType().Name}");
        }
        catch (Exception exception)
        {
            _disabled = true;
            AppDiagnostics.Logger?.Write($"Discord Rich Presence was disabled after an unexpected error: {exception}");
        }
    }

    public void RunCallbacks()
    {
        if (_disposed || !_started)
        {
            return;
        }

        try
        {
            _native.RunCallbacks();
        }
        catch (Exception exception)
        {
            _started = false;
            _disabled = true;
            AppDiagnostics.Logger?.Write($"Discord Rich Presence was disabled while processing callbacks: {exception}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _native.Dispose();
    }

    private static bool IsOptionalIntegrationFailure(Exception exception) =>
        exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;
}

internal sealed class DiscordSocialSdkNative : IDiscordRichPresenceNative
{
    private readonly ulong _applicationId;
    private NativeObject _client;
    private bool _disposed;
    private bool _started;

    public DiscordSocialSdkNative(ulong applicationId)
    {
        _applicationId = applicationId;
    }

    public void Start(DiscordRichPresenceActivity activity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        NativeMethods.ClientInit(ref _client);
        try
        {
            NativeMethods.ClientSetApplicationId(ref _client, _applicationId);
            UpdateActivity(activity);
            _started = true;
        }
        catch
        {
            NativeMethods.ClientDrop(ref _client);
            _client = default;
            throw;
        }
    }

    public void RunCallbacks()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            NativeMethods.RunCallbacks();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_client.Opaque != IntPtr.Zero)
        {
            NativeMethods.ClientDrop(ref _client);
            _client = default;
        }
    }

    private void UpdateActivity(DiscordRichPresenceActivity activity)
    {
        NativeObject nativeActivity = default;
        NativeObject assets = default;
        NativeMethods.ActivityInit(ref nativeActivity);
        NativeMethods.ActivityAssetsInit(ref assets);
        try
        {
            using Utf8String details = new(activity.Details);
            using Utf8String largeImage = new(activity.LargeImage);
            using Utf8String largeText = new(activity.LargeText);
            using Utf8String smallImage = new(activity.SmallImage);
            using Utf8String smallText = new(activity.SmallText);
            NativeString detailsValue = details.Value;
            NativeString largeImageValue = largeImage.Value;
            NativeString largeTextValue = largeText.Value;
            NativeString smallImageValue = smallImage.Value;
            NativeString smallTextValue = smallText.Value;

            NativeMethods.ActivitySetType(ref nativeActivity, DiscordActivityType.Playing);
            NativeMethods.ActivitySetDetails(ref nativeActivity, ref detailsValue);
            NativeMethods.ActivityAssetsSetLargeImage(ref assets, ref largeImageValue);
            NativeMethods.ActivityAssetsSetLargeText(ref assets, ref largeTextValue);
            NativeMethods.ActivityAssetsSetSmallImage(ref assets, ref smallImageValue);
            NativeMethods.ActivityAssetsSetSmallText(ref assets, ref smallTextValue);
            NativeMethods.ActivitySetAssets(ref nativeActivity, ref assets);
            NativeMethods.ClientUpdateRichPresence(
                ref _client,
                ref nativeActivity,
                NativeMethods.UpdateCallback,
                NativeMethods.NoopFreeCallback,
                IntPtr.Zero);
        }
        finally
        {
            NativeMethods.ActivityAssetsDrop(ref assets);
            NativeMethods.ActivityDrop(ref nativeActivity);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeObject
    {
        public IntPtr Opaque;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeString
    {
        public IntPtr Pointer;
        public nuint Length;
    }

    private sealed class Utf8String : IDisposable
    {
        public Utf8String(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            Pointer = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, Pointer, bytes.Length);
            Value = new NativeString { Pointer = Pointer, Length = (nuint)bytes.Length };
        }

        public IntPtr Pointer { get; }

        public NativeString Value { get; }

        public void Dispose()
        {
            Marshal.FreeHGlobal(Pointer);
        }
    }

    private enum DiscordActivityType
    {
        Playing = 0,
    }

    private static class NativeMethods
    {
        private const string LibraryName = "discord_partner_sdk";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void UpdateRichPresenceCallback(IntPtr result, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void FreeCallback(IntPtr userData);

        internal static readonly UpdateRichPresenceCallback UpdateCallback = OnUpdateCompleted;
        internal static readonly FreeCallback NoopFreeCallback = _ => { };

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Discord_RunCallbacks")]
        internal static extern void RunCallbacks();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Discord_Client_Init")]
        internal static extern void ClientInit(ref NativeObject client);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Discord_Client_Drop")]
        internal static extern void ClientDrop(ref NativeObject client);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Discord_Client_SetApplicationId")]
        internal static extern void ClientSetApplicationId(ref NativeObject client, ulong applicationId);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Discord_Client_UpdateRichPresence")]
        internal static extern void ClientUpdateRichPresence(
            ref NativeObject client,
            ref NativeObject activity,
            UpdateRichPresenceCallback callback,
            FreeCallback callbackUserDataFree,
            IntPtr callbackUserData);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Discord_Activity_Init")]
        internal static extern void ActivityInit(ref NativeObject activity);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Discord_Activity_Drop")]
        internal static extern void ActivityDrop(ref NativeObject activity);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Discord_Activity_SetType")]
        internal static extern void ActivitySetType(ref NativeObject activity, DiscordActivityType activityType);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Discord_Activity_SetDetails")]
        internal static extern void ActivitySetDetails(ref NativeObject activity, ref NativeString details);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Discord_Activity_SetAssets")]
        internal static extern void ActivitySetAssets(ref NativeObject activity, ref NativeObject assets);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Discord_ActivityAssets_Init")]
        internal static extern void ActivityAssetsInit(ref NativeObject assets);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Discord_ActivityAssets_Drop")]
        internal static extern void ActivityAssetsDrop(ref NativeObject assets);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Discord_ActivityAssets_SetLargeImage")]
        internal static extern void ActivityAssetsSetLargeImage(ref NativeObject assets, ref NativeString value);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Discord_ActivityAssets_SetLargeText")]
        internal static extern void ActivityAssetsSetLargeText(ref NativeObject assets, ref NativeString value);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Discord_ActivityAssets_SetSmallImage")]
        internal static extern void ActivityAssetsSetSmallImage(ref NativeObject assets, ref NativeString value);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Discord_ActivityAssets_SetSmallText")]
        internal static extern void ActivityAssetsSetSmallText(ref NativeObject assets, ref NativeString value);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Discord_ClientResult_Successful")]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool ClientResultSuccessful(IntPtr result);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Discord_ClientResult_Drop")]
        internal static extern void ClientResultDrop(IntPtr result);

        private static void OnUpdateCompleted(IntPtr result, IntPtr userData)
        {
            try
            {
                if (!ClientResultSuccessful(result))
                {
                    AppDiagnostics.Logger?.Write("Discord Rich Presence update was rejected by Discord.");
                }
            }
            catch (Exception exception)
            {
                AppDiagnostics.Logger?.Write($"Discord Rich Presence update callback failed: {exception}");
            }
            finally
            {
                if (result != IntPtr.Zero)
                {
                    ClientResultDrop(result);
                }
            }
        }
    }
}
