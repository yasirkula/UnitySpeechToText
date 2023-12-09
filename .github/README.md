# Unity Speech to Text Plugin for Android & iOS

**Discord:** https://discord.gg/UJJt549AaV

**[GitHub Sponsors ☕](https://github.com/sponsors/yasirkula)**

This plugin helps you convert speech to text on Android (all versions) and iOS 10+. Offline speech recognition is supported on Android 23+ and iOS 13+ if the target language's speech recognition model is present on the device.

Note that continuous speech detection isn't supported so the speech recognition sessions automatically end after a short break in the speech or when the OS-determined time limits are reached. 

## INSTALLATION

There are 4 ways to install this plugin:

- import [SpeechToText.unitypackage](https://github.com/yasirkula/UnitySpeechToText/releases) via *Assets-Import Package*
- clone/[download](https://github.com/yasirkula/UnitySpeechToText/archive/master.zip) this repository and move the *Plugins* folder to your Unity project's *Assets* folder
- *(via Package Manager)* add the following line to *Packages/manifest.json*:
  - `"com.yasirkula.speechtotext": "https://github.com/yasirkula/UnitySpeechToText.git",`
- *(via [OpenUPM](https://openupm.com))* after installing [openupm-cli](https://github.com/openupm/openupm-cli), run the following command:
  - `openupm add com.yasirkula.speechtotext`

### iOS Setup

There are two ways to set up the plugin on iOS:

**a. Automated Setup for iOS**

- *(optional)* change the values of **Speech Recognition Usage Description** and **Microphone Usage Description** at *Project Settings/yasirkula/Speech to Text*

**b. Manual Setup for iOS**

- see: https://github.com/yasirkula/UnitySpeechToText/wiki/Manual-Setup-for-iOS

## KNOWN ISSUES

- Speech session returned [error code 12](https://developer.android.com/reference/android/speech/SpeechRecognizer#ERROR_LANGUAGE_NOT_SUPPORTED) on a single Android test device (regardless of target language) and couldn't be started

## HOW TO

**NOTE:** The codebase is documented using XML comments so this section will only briefly mention the functions.

You should first initialize the plugin via `SpeechToText.Initialize( string preferredLanguage = null )`. If you don't provide a preferred language (in the format "*en-US*"), the device's default language is used. You can check if a language is supported via `SpeechToText.IsLanguageSupported( string language )`.

After initialization, you can query `SpeechToText.IsServiceAvailable( bool preferOfflineRecognition = false )` and `SpeechToText.IsBusy()` to see if a speech recognition session can be started. Most operations will fail while the service is unavailable or busy.

Before starting a speech recognition session, you must make sure that the necessary permissions are granted via `SpeechToText.CheckPermission()` and `SpeechToText.RequestPermissionAsync( PermissionCallback callback )` functions. If permission is *Denied*, you can call `SpeechToText.OpenSettings()` to automatically open the app's Settings from where the user can grant the necessary permissions manually (Android: Microphone, iOS: Microphone and Speech Recognition). On Android, the speech recognition system also requires the Google app to have Microphone permission. If not, its result callback will return error code 9. In that scenario, you can notify the user and call `SpeechToText.OpenGoogleAppSettings()` to automatically open the Google app's Settings from where the user can grant it the Microphone permission manually.

To start a speech recognition session, you can call `SpeechToText.Start( ISpeechToTextListener listener, bool useFreeFormLanguageModel = true, bool preferOfflineRecognition = false )`. Normally, sessions end automatically after a short break in the speech but you can also stop the session manually via `SpeechToText.ForceStop()` (processes the speech input so far) or `SpeechToText.Cancel()` (doesn't process any speech input and immediately invokes the result callback with error code 0). The `ISpeechToTextListener` interface has the following functions:

- `OnReadyForSpeech()`
- `OnBeginningOfSpeech()`
- `OnVoiceLevelChanged( float normalizedVoiceLevel )`
- `OnPartialResultReceived( string spokenText )`
- `OnResultReceived( string spokenText, int? errorCode )`

## EXAMPLE CODE

```csharp
using UnityEngine;
using UnityEngine.UI;

public class SpeechToTextDemo : MonoBehaviour, ISpeechToTextListener
{
	public Text SpeechText;
	public Button StartSpeechToTextButton, StopSpeechToTextButton;
	public Slider VoiceLevelSlider;
	public bool PreferOfflineRecognition;

	private float normalizedVoiceLevel;

	private void Awake()
	{
		SpeechToText.Initialize( "en-US" );

		StartSpeechToTextButton.onClick.AddListener( StartSpeechToText );
		StopSpeechToTextButton.onClick.AddListener( StopSpeechToText );
	}

	private void Update()
	{
		StartSpeechToTextButton.interactable = SpeechToText.IsServiceAvailable( PreferOfflineRecognition ) && !SpeechToText.IsBusy();
		StopSpeechToTextButton.interactable = SpeechToText.IsBusy();

		// You may also apply some noise to the voice level for a more fluid animation (e.g. via Mathf.PerlinNoise)
		VoiceLevelSlider.value = Mathf.Lerp( VoiceLevelSlider.value, normalizedVoiceLevel, 15f * Time.unscaledDeltaTime );
	}

	public void ChangeLanguage( string preferredLanguage )
	{
		if( !SpeechToText.Initialize( preferredLanguage ) )
			SpeechText.text = "Couldn't initialize with language: " + preferredLanguage;
	}

	public void StartSpeechToText()
	{
		SpeechToText.RequestPermissionAsync( ( permission ) =>
		{
			if( permission == SpeechToText.Permission.Granted )
			{
				if( SpeechToText.Start( this, preferOfflineRecognition: PreferOfflineRecognition ) )
					SpeechText.text = "";
				else
					SpeechText.text = "Couldn't start speech recognition session!";
			}
			else
				SpeechText.text = "Permission is denied!";
		} );
	}

	public void StopSpeechToText()
	{
		SpeechToText.ForceStop();
	}

	void ISpeechToTextListener.OnReadyForSpeech()
	{
		Debug.Log( "OnReadyForSpeech" );
	}

	void ISpeechToTextListener.OnBeginningOfSpeech()
	{
		Debug.Log( "OnBeginningOfSpeech" );
	}

	void ISpeechToTextListener.OnVoiceLevelChanged( float normalizedVoiceLevel )
	{
		// Note that On Android, voice detection starts with a beep sound and it can trigger this callback. You may want to ignore this callback for ~0.5s on Android.
		this.normalizedVoiceLevel = normalizedVoiceLevel;
	}

	void ISpeechToTextListener.OnPartialResultReceived( string spokenText )
	{
		Debug.Log( "OnPartialResultReceived: " + spokenText );
		SpeechText.text = spokenText;
	}

	void ISpeechToTextListener.OnResultReceived( string spokenText, int? errorCode )
	{
		Debug.Log( "OnResultReceived: " + spokenText + ( errorCode.HasValue ? ( " --- Error: " + errorCode ) : "" ) );
		SpeechText.text = spokenText;
		normalizedVoiceLevel = 0f;

		// Recommended approach:
		// - If errorCode is 0, session was aborted via SpeechToText.Cancel. Handle the case appropriately.
		// - If errorCode is 9, notify the user that they must grant Microphone permission to the Google app and call SpeechToText.OpenGoogleAppSettings.
		// - If the speech session took shorter than 1 seconds (should be an error) or a null/empty spokenText is returned, prompt the user to try again (note that if
		//   errorCode is 6, then the user hasn't spoken and the session has timed out as expected).
	}
}
```
