package com.yasirkula.unity;

public interface SpeechToTextListener
{
	void OnReadyForSpeech();
	void OnBeginningOfSpeech();
	void OnVoiceLevelChanged( float rmsdB );
	void OnPartialResultReceived( String spokenText );
	void OnResultReceived( String spokenText, int errorCode );
}