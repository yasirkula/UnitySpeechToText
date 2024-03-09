#if UNITY_EDITOR || UNITY_IOS
using System.Collections;
using UnityEngine;

namespace SpeechToTextNamespace
{
	public class STTInteractionCallbackiOS : MonoBehaviour
	{
		private static STTInteractionCallbackiOS instance;
		private ISpeechToTextListener listener;
		private bool beginningOfSpeechInvoked;
		private Coroutine voiceLevelChangeDetectionCoroutine;

#if !UNITY_EDITOR && UNITY_IOS
		[System.Runtime.InteropServices.DllImport( "__Internal" )]
		private static extern float _SpeechToText_GetAudioRmsdB();
#endif

		public static void Initialize( ISpeechToTextListener listener )
		{
			if( instance == null )
			{
				instance = new GameObject( "STTInteractionCallbackiOS" ).AddComponent<STTInteractionCallbackiOS>();
				DontDestroyOnLoad( instance.gameObject );
			}
			else if( instance.listener != null )
				instance.listener.OnResultReceived( null, 8 );

			instance.listener = listener;
			instance.beginningOfSpeechInvoked = false;

			if( instance.voiceLevelChangeDetectionCoroutine == null )
				instance.voiceLevelChangeDetectionCoroutine = instance.StartCoroutine( instance.VoiceLevelChangeDetectionCoroutine() );

			listener.OnReadyForSpeech();
		}

		private IEnumerator VoiceLevelChangeDetectionCoroutine()
		{
			float lastRmsDB = -1f;
			while( listener != null )
			{
#if !UNITY_EDITOR && UNITY_IOS
				float rmsDB = _SpeechToText_GetAudioRmsdB();
#else
				float rmsDB = 0f;
#endif
				if( rmsDB != lastRmsDB )
				{
					lastRmsDB = rmsDB;
					OnVoiceLevelChanged( rmsDB );
				}

				yield return null;
			}

			voiceLevelChangeDetectionCoroutine = null;
		}

		[UnityEngine.Scripting.Preserve]
		/// <param name="rmsdB">Root Mean Square (RMS) dB between range [0, 160] (0: quiet, 160: loud)</param>
		public void OnVoiceLevelChanged( float rmsdB )
		{
			// Convert [130, 150] dB range to [0, 1]
			if( listener != null )
				listener.OnVoiceLevelChanged( Mathf.Clamp01( ( rmsdB - 130f ) / 20f ) );
		}

		[UnityEngine.Scripting.Preserve]
		public void OnPartialResultReceived( string spokenText )
		{
			if( listener != null )
			{
				// Potentially more accurate way of determining the beginning of speech: https://stackoverflow.com/a/46325305
				if( !beginningOfSpeechInvoked )
				{
					beginningOfSpeechInvoked = true;
					listener.OnBeginningOfSpeech();
				}

				if( !string.IsNullOrEmpty( spokenText ) )
					listener.OnPartialResultReceived( spokenText );
			}
		}

		[UnityEngine.Scripting.Preserve]
		public void OnResultReceived( string spokenText )
		{
			ISpeechToTextListener _listener = listener;
			listener = null;

			if( _listener != null )
				_listener.OnResultReceived( !string.IsNullOrEmpty( spokenText ) ? spokenText : null, null );
		}

		[UnityEngine.Scripting.Preserve]
		public void OnError( string error )
		{
			ISpeechToTextListener _listener = listener;
			listener = null;

			if( _listener != null )
			{
				int errorCode;
				if( !int.TryParse( error, out errorCode ) )
					errorCode = -1;

				_listener.OnResultReceived( null, errorCode );
			}
		}
	}
}
#endif