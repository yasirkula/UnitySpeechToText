#if UNITY_EDITOR || UNITY_ANDROID
using UnityEngine;

namespace SpeechToTextNamespace
{
	public class STTInteractionCallbackAndroid : AndroidJavaProxy
	{
		private readonly ISpeechToTextListener listener;
		private readonly STTCallbackHelper callbackHelper;

		public STTInteractionCallbackAndroid( ISpeechToTextListener listener ) : base( "com.yasirkula.unity.SpeechToTextListener" )
		{
			this.listener = listener;
			callbackHelper = new GameObject( "STTCallbackHelper" ).AddComponent<STTCallbackHelper>();
		}

		[UnityEngine.Scripting.Preserve]
		public void OnReadyForSpeech()
		{
			callbackHelper.CallOnMainThread( listener.OnReadyForSpeech );
		}

		[UnityEngine.Scripting.Preserve]
		public void OnBeginningOfSpeech()
		{
			callbackHelper.CallOnMainThread( listener.OnBeginningOfSpeech );
		}

		[UnityEngine.Scripting.Preserve]
		/// <param name="rmsdB">Root Mean Square (RMS) dB between range [-2, 10] (-2: quiet, 10: loud)</param>
		public void OnVoiceLevelChanged( float rmsdB )
		{
			// Credit: https://stackoverflow.com/a/14124484/2373034
			float normalizedVoiceLevel = Mathf.Clamp01( 0.1f * Mathf.Pow( 10f, rmsdB / 10f ) );
			callbackHelper.CallOnMainThread( () => listener.OnVoiceLevelChanged( normalizedVoiceLevel ) );
		}

		[UnityEngine.Scripting.Preserve]
		public void OnPartialResultReceived( string spokenText )
		{
			if( !string.IsNullOrEmpty( spokenText ) )
				callbackHelper.CallOnMainThread( () => listener.OnPartialResultReceived( spokenText ) );
		}

		[UnityEngine.Scripting.Preserve]
		public void OnResultReceived( string spokenText, int errorCode )
		{
			// ERROR_NO_MATCH (7) error code is thrown instead of ERROR_SPEECH_TIMEOUT (6) if the user doesn't speak. ERROR_NO_MATCH is also
			// thrown if the system can't understand the user's speech but I unfortunately couldn't find a way to distinguish between
			// these two cases. So, ERROR_NO_MATCH is always considered as ERROR_SPEECH_TIMEOUT for the time being.
			if( errorCode == 7 )
				errorCode = 6;

			callbackHelper.CallOnMainThread( () =>
			{
				try
				{
					listener.OnResultReceived( !string.IsNullOrEmpty( spokenText ) ? spokenText : null, ( errorCode >= 0 ) ? (int?) errorCode : null );
				}
				finally
				{
					Object.DestroyImmediate( callbackHelper.gameObject );
				}
			} );
		}
	}
}
#endif