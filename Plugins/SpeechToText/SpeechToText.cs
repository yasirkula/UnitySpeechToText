using UnityEngine;
#if UNITY_2018_4_OR_NEWER && !SPEECH_TO_TEXT_DISABLE_ASYNC_FUNCTIONS
using System.Threading.Tasks;
#endif
#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
using SpeechToTextNamespace;
#endif

public static class SpeechToText
{
	public enum Permission
	{
		/// <summary>
		/// Permission is permanently denied. User must grant the permission from the app's Settings (see <see cref="OpenSettings"/>).
		/// </summary>
		Denied = 0,
		/// <summary>
		/// Permission is granted.
		/// </summary>
		Granted = 1,
		/// <summary>
		/// Permission isn't granted but it can be asked via <see cref="RequestPermissionAsync"/>.
		/// </summary>
		ShouldAsk = 2
	};

	public enum LanguageSupport
	{
		/// <summary>
		/// Language support couldn't be determined (Android only).
		/// </summary>
		Unknown = -1,
		/// <summary>
		/// Language is not supported.
		/// </summary>
		NotSupported = 0,
		/// <summary>
		/// Language is supported.
		/// </summary>
		Supported = 1,
		/// <summary>
		/// Happens when e.g. the queried language is "en" but the speech recognition service returns "en-US" instead of "en" (Android only).
		/// </summary>
		LikelySupported = 2
	};

	public delegate void PermissionCallback( Permission permission );

	#region Platform Specific Elements
#if !UNITY_EDITOR && UNITY_ANDROID
	private static AndroidJavaClass m_ajc = null;
	private static AndroidJavaClass AJC
	{
		get
		{
			if( m_ajc == null )
				m_ajc = new AndroidJavaClass( "com.yasirkula.unity.SpeechToText" );

			return m_ajc;
		}
	}

	private static AndroidJavaObject m_context = null;
	private static AndroidJavaObject Context
	{
		get
		{
			if( m_context == null )
			{
				using( AndroidJavaObject unityClass = new AndroidJavaClass( "com.unity3d.player.UnityPlayer" ) )
					m_context = unityClass.GetStatic<AndroidJavaObject>( "currentActivity" );
			}

			return m_context;
		}
	}

	private static string preferredLanguage;
#elif !UNITY_EDITOR && UNITY_IOS
	[System.Runtime.InteropServices.DllImport( "__Internal" )]
	private static extern int _SpeechToText_Initialize( string language );

	[System.Runtime.InteropServices.DllImport( "__Internal" )]
	private static extern int _SpeechToText_Start( int useFreeFormLanguageModel, int preferOfflineRecognition );

	[System.Runtime.InteropServices.DllImport( "__Internal" )]
	private static extern void _SpeechToText_Stop();

	[System.Runtime.InteropServices.DllImport( "__Internal" )]
	private static extern void _SpeechToText_Cancel();

	[System.Runtime.InteropServices.DllImport( "__Internal" )]
	private static extern int _SpeechToText_IsLanguageSupported( string language );

	[System.Runtime.InteropServices.DllImport( "__Internal" )]
	private static extern int _SpeechToText_IsServiceAvailable( int preferOfflineRecognition );

	[System.Runtime.InteropServices.DllImport( "__Internal" )]
	private static extern int _SpeechToText_IsBusy();

	[System.Runtime.InteropServices.DllImport( "__Internal" )]
	private static extern int _SpeechToText_CheckPermission();

	[System.Runtime.InteropServices.DllImport( "__Internal" )]
	private static extern int _SpeechToText_RequestPermission( int asyncMode );

	[System.Runtime.InteropServices.DllImport( "__Internal" )]
	private static extern void _SpeechToText_OpenSettings();
#elif UNITY_EDITOR
	private static STTCallbackHelper speechSessionEmulator;
	private static ISpeechToTextListener speechSessionEmulatorListener;
#endif
	#endregion

	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.AfterSceneLoad )]
	private static void InitializeOnLoad()
	{
#if !UNITY_EDITOR && UNITY_ANDROID
		AJC.CallStatic( "InitializeSupportedLanguages", Context );
#endif
	}

	/// <summary>
	/// Initializes speech recognition service with the preferred language or the default device language.
	/// If the preferred language isn't available, the default device language may be used by the system as fallback.
	/// </summary>
	/// <param name="preferredLanguage">Must be in the format: "en-US".</param>
	/// <returns>True, if the service is initialized successfully.</returns>
	public static bool Initialize( string preferredLanguage = null )
	{
#if !UNITY_EDITOR && UNITY_ANDROID
		SpeechToText.preferredLanguage = preferredLanguage;
		return true;
#elif !UNITY_EDITOR && UNITY_IOS
		return _SpeechToText_Initialize( preferredLanguage ?? "" ) == 1;
#else
		return true;
#endif
	}

	/// <param name="language">Must be in the format: "en-US".</param>
	public static LanguageSupport IsLanguageSupported( string language )
	{
#if !UNITY_EDITOR && UNITY_ANDROID
		return (LanguageSupport) AJC.CallStatic<int>( "IsLanguageSupported", language ?? "" );
#elif !UNITY_EDITOR && UNITY_IOS
		return (LanguageSupport) _SpeechToText_IsLanguageSupported( language ?? "" );
#else
		return LanguageSupport.Supported;
#endif
	}

	/// <summary>
	/// Checks if speech recognition service is available. Must be called <b>AFTER</b> <see cref="Initialize"/>.
	/// </summary>
	/// <param name="preferOfflineRecognition">
	/// If <c>true</c>, checks if on-device speech recognition is supported.
	/// On Android, it isn't guaranteed that offline speech recognition will actually be used, even if this function returns <c>true</c>.
	/// Also, there is currently no way to check if the target language is actually downloaded on Android (if not, this function may
	/// return <c>true</c> but the speech recognition session will fail). So this function isn't reliable for offline recognition on Android.
	/// </param>
	public static bool IsServiceAvailable( bool preferOfflineRecognition = false )
	{
#if !UNITY_EDITOR && UNITY_ANDROID
		return AJC.CallStatic<bool>( "IsServiceAvailable", Context, preferOfflineRecognition );
#elif !UNITY_EDITOR && UNITY_IOS
		return _SpeechToText_IsServiceAvailable( preferOfflineRecognition ? 1 : 0 ) == 1;
#else
		return true;
#endif
	}

	/// <returns>True, if a speech recognition session is currently in progress. Another session can't be started during that time.</returns>
	public static bool IsBusy()
	{
#if !UNITY_EDITOR && UNITY_ANDROID
		return AJC.CallStatic<bool>( "IsBusy" );
#elif !UNITY_EDITOR && UNITY_IOS
		return _SpeechToText_IsBusy() == 1;
#elif UNITY_EDITOR
		return speechSessionEmulator != null;
#else
		return false;
#endif
	}

	#region Runtime Permissions
	/// <returns>True, if we have permission to start a speech recognition session.</returns>
	public static bool CheckPermission()
	{
#if !UNITY_EDITOR && UNITY_ANDROID
		return AJC.CallStatic<bool>( "CheckPermission", Context );
#elif !UNITY_EDITOR && UNITY_IOS
		return _SpeechToText_CheckPermission() == 1;
#else
		return true;
#endif
	}

	/// <summary>
	/// Requests the necessary permission for speech recognition. Without this permission, <see cref="Start"/> will fail.
	/// </summary>
	public static void RequestPermissionAsync( PermissionCallback callback )
	{
#if !UNITY_EDITOR && UNITY_ANDROID
		STTPermissionCallbackAsyncAndroid nativeCallback = new STTPermissionCallbackAsyncAndroid( callback );
		AJC.CallStatic<bool>( "RequestPermission", Context, nativeCallback );
#elif !UNITY_EDITOR && UNITY_IOS
		STTPermissionCallbackiOS.Initialize( callback );
		_SpeechToText_RequestPermission( 1 );
#else
		callback( Permission.Granted );
#endif
	}

#if UNITY_2018_4_OR_NEWER && !SPEECH_TO_TEXT_DISABLE_ASYNC_FUNCTIONS
	/// <inheritdoc cref="RequestPermissionAsync(PermissionCallback)"/>
	public static Task<Permission> RequestPermissionAsync()
	{
		TaskCompletionSource<Permission> tcs = new TaskCompletionSource<Permission>();
		RequestPermissionAsync( ( permission ) => tcs.SetResult( permission ) );
		return tcs.Task;
	}
#endif

	/// <summary>
	/// Opens the app's Settings from where the user can grant the necessary permissions manually
	/// (Android: Record Audio, iOS: Speech Recognition and Microphone).
	/// </summary>
	public static void OpenSettings()
	{
#if !UNITY_EDITOR && UNITY_ANDROID
		AJC.CallStatic( "OpenSettings", Context, "" );
#elif !UNITY_EDITOR && UNITY_IOS
		_SpeechToText_OpenSettings();
#endif
	}

	/// <summary>
	/// Opens the Google app's Settings from where the user can grant the Microphone permission to it on Android.
	/// Can be called if <see cref="ISpeechToTextListener.OnResultReceived"/> returns error code 9.
	/// </summary>
	public static void OpenGoogleAppSettings()
	{
#if !UNITY_EDITOR && UNITY_ANDROID
		AJC.CallStatic( "OpenSettings", Context, "com.google.android.googlequicksearchbox" );
#endif
	}
	#endregion

	#region Speech-to-text Functions
	/// <summary>
	/// Attempts to start a speech recognition session. Must be called <b>AFTER</b> <see cref="Initialize"/>.
	/// </summary>
	/// <param name="listener">The listener whose callback functions will be invoked.</param>
	/// <param name="useFreeFormLanguageModel">
	/// If <c>true</c>, free-form/dictation language model will be used (more suited for general purpose speech).
	/// Otherwise, search-focused language model will be used (specialized in search terms).
	/// </param>
	/// <param name="preferOfflineRecognition">
	/// If <c>true</c> and the active language supports on-device speech recognition, it'll be used.
	/// Note that offline speech recognition may not be very accurate. Requires Android 23+ or iOS 13+.
	/// </param>
	/// <returns>True, if session is created successfully. If permission isn't granted yet, returns false (see <see cref="RequestPermissionAsync"/>).</returns>
	public static bool Start( ISpeechToTextListener listener, bool useFreeFormLanguageModel = true, bool preferOfflineRecognition = false )
	{
#if !UNITY_EDITOR && UNITY_ANDROID
		STTInteractionCallbackAndroid nativeCallback = new STTInteractionCallbackAndroid( listener );
		return AJC.CallStatic<bool>( "Start", Context, nativeCallback, preferredLanguage ?? "", useFreeFormLanguageModel, true, preferOfflineRecognition );
#elif !UNITY_EDITOR && UNITY_IOS
		if( _SpeechToText_Start( useFreeFormLanguageModel ? 1 : 0, preferOfflineRecognition ? 1 : 0 ) == 1 )
		{
			STTInteractionCallbackiOS.Initialize( listener );
			return true;
		}

		return false;
#elif UNITY_EDITOR
		speechSessionEmulatorListener = listener;
		speechSessionEmulator = new GameObject( "SpeechToText Emulator" ).AddComponent<STTCallbackHelper>();
		speechSessionEmulator.StartCoroutine( EmulateSpeechOnEditor() );

		return true;
#else
		return true;
#endif
	}

	/// <summary>
	/// If a speech recognition session is in progress, stops it manually. Normally, a session is automatically stopped after the user stops speaking for a short while.
	/// Note that on some Android versions, this call may have no effect (welcome to Android ecosystem): https://issuetracker.google.com/issues/158198432
	/// </summary>
	public static void ForceStop()
	{
#if !UNITY_EDITOR && UNITY_ANDROID
		AJC.CallStatic( "Stop", Context );
#elif !UNITY_EDITOR && UNITY_IOS
		_SpeechToText_Stop();
#elif UNITY_EDITOR
		StopEmulateSpeechOnEditor( "Hello world", null );
#endif
	}

	/// <summary>
	/// If a speech recognition session is in progress, cancels it. Canceled sessions return an error code of 0 in their <see cref="ISpeechToTextListener.OnResultReceived"/> callback.
	/// </summary>
	public static void Cancel()
	{
#if !UNITY_EDITOR && UNITY_ANDROID
		AJC.CallStatic( "Cancel", Context );
#elif !UNITY_EDITOR && UNITY_IOS
		_SpeechToText_Cancel();
#elif UNITY_EDITOR
		StopEmulateSpeechOnEditor( null, 0 );
#endif
	}

#if UNITY_EDITOR
	private static System.Collections.IEnumerator EmulateSpeechOnEditor()
	{
		try
		{
			speechSessionEmulator.StartCoroutine( EmulateVoiceLevelChangeOnEditor() );

			yield return new WaitForSecondsRealtime( 0.25f );
			speechSessionEmulatorListener.OnReadyForSpeech();
			yield return new WaitForSecondsRealtime( 0.5f );
			speechSessionEmulatorListener.OnBeginningOfSpeech();
			yield return new WaitForSecondsRealtime( 0.33f );
			speechSessionEmulatorListener.OnPartialResultReceived( "Hello" );
			yield return new WaitForSecondsRealtime( 0.33f );
			speechSessionEmulatorListener.OnPartialResultReceived( "Hello world" );
			yield return new WaitForSecondsRealtime( 0.5f );
		}
		finally
		{
			StopEmulateSpeechOnEditor( "Hello world", null );
		}
	}

	private static System.Collections.IEnumerator EmulateVoiceLevelChangeOnEditor()
	{
		yield return new WaitForSecondsRealtime( 0.25f );

		while( true )
		{
			speechSessionEmulatorListener.OnVoiceLevelChanged( Mathf.Clamp01( Mathf.PerlinNoise( Time.unscaledTime * 4f, Time.unscaledTime * -2f ) ) );

			for( int i = 0; i < 3; i++ )
				yield return null;
		}
	}

	private static void StopEmulateSpeechOnEditor( string spokenText, int? errorCode )
	{
		if( speechSessionEmulator == null )
			return;

		Object.DestroyImmediate( speechSessionEmulator.gameObject );
		speechSessionEmulatorListener.OnResultReceived( spokenText, errorCode );
		speechSessionEmulator = null;
		speechSessionEmulatorListener = null;
	}
#endif
	#endregion
}