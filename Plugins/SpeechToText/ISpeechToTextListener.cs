public interface ISpeechToTextListener
{
	/// <summary>
	/// Invoked when speech recognition service starts listening to the user's speech input. On iOS, it's invoked immediately.
	/// </summary>
	void OnReadyForSpeech();

	/// <summary>
	/// Invoked when speech recognition service detects a speech for the first time. On iOS, it's called just before the first invocation of <see cref="OnPartialResultReceived"/>.
	/// </summary>
	void OnBeginningOfSpeech();

	/// <summary>
	/// Invoked regularly as the user speaks to report their current voice level.
	/// </summary>
	/// <param name="normalizedVoiceLevel">User's voice level in [0, 1] range (0: quiet, 1: loud)</param>
	void OnVoiceLevelChanged( float normalizedVoiceLevel );

	/// <summary>
	/// Invoked regularly as the user speaks to report their speech input so far.
	/// </summary>
	void OnPartialResultReceived( string spokenText );

	/// <summary>
	/// Invoked after the speech recognition is finalized.
	/// </summary>
	/// <param name="errorCode">
	/// If not null, an error has occurred. On Android, all error codes are listed here: https://developer.android.com/reference/android/speech/SpeechRecognizer#constants_1 <br/>
	/// Special error codes:<br/>
	/// - 0: <see cref="Cancel"/> is called.<br/>
	/// - 6: User hasn't spoken and the speech session has timed out.<br/>
	/// - 9: Google app that processes the speech doesn't have Microphone permission on Android. User can be informed that they should grant the permission
	///      from Google app's Settings and, for convenience, that Settings page can be opened programmatically via <see cref="SpeechToText.OpenGoogleAppSettings"/>.
	///      See: https://stackoverflow.com/a/48006238/2373034
	/// </param>
	void OnResultReceived( string spokenText, int? errorCode );
}