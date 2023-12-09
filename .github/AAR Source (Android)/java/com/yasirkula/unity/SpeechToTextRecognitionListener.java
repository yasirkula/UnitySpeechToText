package com.yasirkula.unity;

import android.os.Bundle;
import android.speech.RecognitionListener;
import android.speech.RecognizerIntent;
import android.speech.SpeechRecognizer;
import android.util.Log;

import java.util.ArrayList;

public class SpeechToTextRecognitionListener implements RecognitionListener
{
	private final SpeechToTextListener unityInterface;
	private boolean isResultSent;
	private String lastResult = "";

	public SpeechToTextRecognitionListener( SpeechToTextListener unityInterface )
	{
		this.unityInterface = unityInterface;
	}

	private void SendResult( String result, int errorCode )
	{
		if( !isResultSent )
		{
			isResultSent = true;
			unityInterface.OnResultReceived( result, errorCode );
		}
	}

	public boolean IsFinished()
	{
		return isResultSent;
	}

	public void OnSpeechRecognizerCanceled( boolean isCanceledByUser )
	{
		SendResult( lastResult, isCanceledByUser ? 0 : SpeechRecognizer.ERROR_RECOGNIZER_BUSY );
	}

	@Override
	public void onReadyForSpeech( Bundle params )
	{
		if( !isResultSent )
			unityInterface.OnReadyForSpeech();
	}

	@Override
	public void onBeginningOfSpeech()
	{
		if( !isResultSent )
			unityInterface.OnBeginningOfSpeech();
	}

	@Override
	public void onResults( Bundle results )
	{
		SendResult( GetMostPromisingResult( results ), -1 );
	}

	@Override
	public void onPartialResults( Bundle partialResults )
	{
		if( !isResultSent )
			unityInterface.OnPartialResultReceived( GetMostPromisingResult( partialResults ) );
	}

	private String GetMostPromisingResult( Bundle resultsBundle )
	{
		ArrayList<String> results = resultsBundle.getStringArrayList( SpeechRecognizer.RESULTS_RECOGNITION );
		if( results != null && results.size() > 0 )
		{
			lastResult = results.get( 0 );
			if( results.size() > 1 )
			{
				// Try to get the result with the highest confidence score
				float[] confidenceScores = resultsBundle.getFloatArray( RecognizerIntent.EXTRA_CONFIDENCE_SCORES );
				if( confidenceScores != null && confidenceScores.length >= results.size() )
				{
					float highestConfidenceScore = confidenceScores[0];
					for( int i = 1; i < confidenceScores.length; i++ )
					{
						if( confidenceScores[i] > highestConfidenceScore )
						{
							highestConfidenceScore = confidenceScores[i];
							lastResult = results.get( i );
						}
					}
				}
			}
		}

		if( lastResult == null )
			lastResult = "";

		return lastResult;
	}

	@Override
	public void onError( int error )
	{
		// Error codes: https://developer.android.com/reference/android/speech/SpeechRecognizer
		Log.e( "Unity", "Speech recognition error code: " + error );
		SendResult( lastResult, error );
	}

	@Override
	public void onRmsChanged( float rmsdB )
	{
		if( !isResultSent )
			unityInterface.OnVoiceLevelChanged( rmsdB );
	}

	@Override
	public void onBufferReceived( byte[] buffer )
	{
	}

	@Override
	public void onEndOfSpeech()
	{
	}

	@Override
	public void onEvent( int eventType, Bundle params )
	{
	}
}