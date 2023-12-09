package com.yasirkula.unity;

import android.Manifest;
import android.annotation.TargetApi;
import android.app.Activity;
import android.app.Fragment;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Looper;
import android.provider.Settings;
import android.speech.RecognizerIntent;
import android.speech.SpeechRecognizer;
import android.util.Log;

import java.util.ArrayList;

public class SpeechToText
{
	public static boolean PermissionFreeMode = false;
	public static long MinimumSessionLength = -1; // Observed default value: 5000 milliseconds
	public static long SpeechSilenceTimeout = -1; // Observed default value: 2000 milliseconds

	private static ArrayList<String> supportedLanguages;
	private static SpeechRecognizer speechRecognizer;
	private static SpeechToTextRecognitionListener speechRecognitionListener;

	public static boolean Start( final Context context, final SpeechToTextListener unityInterface, final String language, final boolean useFreeFormLanguageModel, final boolean enablePartialResults, final boolean preferOfflineRecognition )
	{
		if( !IsServiceAvailable( context, preferOfflineRecognition ) || IsBusy() || !RequestPermission( context, null ) )
			return false;

		( (Activity) context ).runOnUiThread( new Runnable()
		{
			@Override
			public void run()
			{
				try
				{
					// Dispose leftover objects from the previous operation
					CancelInternal( false );

					Intent intent = new Intent( RecognizerIntent.ACTION_RECOGNIZE_SPEECH );
					intent.putExtra( RecognizerIntent.EXTRA_LANGUAGE_MODEL, useFreeFormLanguageModel ? RecognizerIntent.LANGUAGE_MODEL_FREE_FORM : RecognizerIntent.LANGUAGE_MODEL_WEB_SEARCH );
					intent.putExtra( RecognizerIntent.EXTRA_MAX_RESULTS, 3 );
					if( language != null && language.length() > 0 )
						intent.putExtra( RecognizerIntent.EXTRA_LANGUAGE, language.replace( '_', '-' ) );
					if( enablePartialResults )
						intent.putExtra( RecognizerIntent.EXTRA_PARTIAL_RESULTS, true );
					if( preferOfflineRecognition && Build.VERSION.SDK_INT >= 23 )
						intent.putExtra( RecognizerIntent.EXTRA_PREFER_OFFLINE, true );
					if( MinimumSessionLength > 0 )
						intent.putExtra( RecognizerIntent.EXTRA_SPEECH_INPUT_MINIMUM_LENGTH_MILLIS, MinimumSessionLength );
					if( SpeechSilenceTimeout > 0 )
					{
						intent.putExtra( RecognizerIntent.EXTRA_SPEECH_INPUT_COMPLETE_SILENCE_LENGTH_MILLIS, SpeechSilenceTimeout );
						intent.putExtra( RecognizerIntent.EXTRA_SPEECH_INPUT_POSSIBLY_COMPLETE_SILENCE_LENGTH_MILLIS, SpeechSilenceTimeout );
					}

					speechRecognizer = preferOfflineRecognition && Build.VERSION.SDK_INT >= 31 ? SpeechRecognizer.createOnDeviceSpeechRecognizer( context ) : SpeechRecognizer.createSpeechRecognizer( context );
					speechRecognitionListener = new SpeechToTextRecognitionListener( unityInterface );
					speechRecognizer.setRecognitionListener( speechRecognitionListener );
					speechRecognizer.startListening( intent );
				}
				catch( Exception e )
				{
					Log.e( "Unity", "Exception:", e );
					CancelInternal( false );
				}
			}
		} );

		return true;
	}

	public static void Stop( Context context )
	{
		if( Looper.myLooper() == Looper.getMainLooper() )
			StopInternal();
		else
		{
			( (Activity) context ).runOnUiThread( new Runnable()
			{
				@Override
				public void run()
				{
					StopInternal();
				}
			} );
		}
	}

	private static void StopInternal()
	{
		if( speechRecognizer != null )
			speechRecognizer.stopListening();
	}

	public static void Cancel( Context context )
	{
		if( Looper.myLooper() == Looper.getMainLooper() )
			CancelInternal( true );
		else
		{
			( (Activity) context ).runOnUiThread( new Runnable()
			{
				@Override
				public void run()
				{
					CancelInternal( true );
				}
			} );
		}
	}

	private static void CancelInternal( boolean isCanceledByUser )
	{
		if( speechRecognizer != null )
		{
			try
			{
				speechRecognitionListener.OnSpeechRecognizerCanceled( isCanceledByUser );
			}
			catch( Exception e )
			{
				Log.e( "Unity", "Exception:", e );
			}
			finally
			{
				speechRecognitionListener = null;
			}

			try
			{
				speechRecognizer.destroy();
			}
			catch( Exception e )
			{
				Log.e( "Unity", "Exception:", e );
			}
			finally
			{
				speechRecognizer = null;
			}
		}
	}

	public static void InitializeSupportedLanguages( final Context context )
	{
		InitializeSupportedLanguagesInternal( context, false );
	}

	private static void InitializeSupportedLanguagesInternal( final Context context, final boolean secondAttempt )
	{
		Intent intent = RecognizerIntent.getVoiceDetailsIntent( context );
		if( intent == null )
			intent = new Intent( RecognizerIntent.ACTION_GET_LANGUAGE_DETAILS );

		// In the first attempt, try to fetch the supported languages list without this hack
		// Credit: https://stackoverflow.com/q/48500077
		if( secondAttempt )
			intent.setPackage( "com.google.android.googlequicksearchbox" );

		try
		{
			context.sendOrderedBroadcast( intent, null, new BroadcastReceiver()
			{
				@Override
				public void onReceive( Context context, Intent intent )
				{
					if( getResultCode() == Activity.RESULT_OK )
					{
						Bundle results = getResultExtras( true );
						supportedLanguages = results.getStringArrayList( RecognizerIntent.EXTRA_SUPPORTED_LANGUAGES );
						if( supportedLanguages == null && !secondAttempt )
							InitializeSupportedLanguagesInternal( context, true );
					}
				}
			}, null, Activity.RESULT_OK, null, null );
		}
		catch( Exception e )
		{
			Log.e( "Unity", "Exception:", e );
		}
	}

	// -1: Unknown, 0: No, 1: Yes, 2: Most likely
	public static int IsLanguageSupported( String language )
	{
		if( language == null || language.length() == 0 )
			return 0;

		if( supportedLanguages != null )
		{
			language = language.replace( '_', '-' );

			if( supportedLanguages.contains( language ) )
				return 1;
			else
			{
				// Match "en" with "en-US" and etc.
				language += "-";

				for( String supportedLanguage : supportedLanguages )
				{
					if( supportedLanguage.startsWith( language ) )
						return 2;
				}
			}

			return 0;
		}

		return -1;
	}

	public static boolean IsServiceAvailable( final Context context, boolean preferOfflineRecognition )
	{
		if( preferOfflineRecognition )
		{
			if( Build.VERSION.SDK_INT >= 31 )
				return SpeechRecognizer.isOnDeviceRecognitionAvailable( context );
			else if( Build.VERSION.SDK_INT < 23 )
				return false;
		}

		return SpeechRecognizer.isRecognitionAvailable( context );
	}

	public static boolean IsBusy()
	{
		return speechRecognitionListener != null && !speechRecognitionListener.IsFinished();
	}

	@TargetApi( Build.VERSION_CODES.M )
	public static boolean CheckPermission( final Context context )
	{
		return PermissionFreeMode || Build.VERSION.SDK_INT < Build.VERSION_CODES.M || context.checkSelfPermission( Manifest.permission.RECORD_AUDIO ) == PackageManager.PERMISSION_GRANTED;
	}

	@TargetApi( Build.VERSION_CODES.M )
	public static boolean RequestPermission( final Context context, final SpeechToTextPermissionReceiver permissionReceiver )
	{
		if( CheckPermission( context ) )
		{
			if( permissionReceiver != null )
				permissionReceiver.OnPermissionResult( 1 );

			return true;
		}

		if( permissionReceiver == null )
			( (Activity) context ).requestPermissions( new String[] { Manifest.permission.RECORD_AUDIO }, 875621 );
		else
		{
			final Fragment request = new SpeechToTextPermissionFragment( permissionReceiver );
			( (Activity) context ).getFragmentManager().beginTransaction().add( 0, request ).commitAllowingStateLoss();
		}

		return false;
	}

	// Credit: https://stackoverflow.com/a/35456817/2373034
	public static void OpenSettings( final Context context, String packageName )
	{
		Uri uri = Uri.fromParts( "package", ( packageName == null || packageName.length() == 0 ) ? context.getPackageName() : packageName, null );

		Intent intent = new Intent();
		intent.setAction( Settings.ACTION_APPLICATION_DETAILS_SETTINGS );
		intent.setData( uri );

		try
		{
			context.startActivity( intent );
		}
		catch( Exception e )
		{
			Log.e( "Unity", "Exception:", e );
		}
	}
}