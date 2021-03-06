﻿#region copyright

/* * * * * * * * * * * * * * * * * * * * * * * * * */
/* Carl Zeiss IMT (IZfM Dresden)                   */
/* Softwaresystem PiWeb                            */
/* (c) Carl Zeiss 2016                             */
/* * * * * * * * * * * * * * * * * * * * * * * * * */

#endregion

using System.IdentityModel.Tokens.Jwt;

namespace Zeiss.IMT.PiWeb.Api.Common.Utilities
{
	#region usings

	using System;
	using System.Collections.Generic;
	using System.IdentityModel.Tokens;
	using System.IO;
	using System.Linq;
	using System.Security.Claims;
	using System.Security.Cryptography;
	using System.Threading.Tasks;
	using IdentityModel;
	using IdentityModel.Client;
	using Newtonsoft.Json;
	using PiWebApi.Annotations;
	using Zeiss.IMT.PiWeb.Api.OAuthService;

	#endregion

	public static class OAuthHelper
	{
		#region constants

		private const string ClientId = "f1ddf74a-7ed1-4963-ab60-a1138a089791";
		private const string ClientSecret = "d2940022-7469-4790-9498-776e3adac79f";
		private const string RedirectUri = "urn:ietf:wg:oauth:2.0:oob";

		private static readonly string _CacheFileDirectory = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.ApplicationData ), @"Zeiss\PiWeb" );
		private static readonly string _CacheFilePath = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.ApplicationData ), @"Zeiss\PiWeb\OpenIdTokens.dat" );

		#endregion

		#region members

		// FUTURE: replace by a real cache with cleanup, this is a memory leak for long running processes
		private static Dictionary<string, OAuthTokenCredential> _AccessTokenCache = new Dictionary<string, OAuthTokenCredential>();
		private static DateTime _FileLastWriteTime;
		private static long _FileLength;

		#endregion

		

		public static JwtSecurityToken DecodeSecurityToken( string jwtEncodedString )
		{
			return new JwtSecurityToken( jwtEncodedString );
		}

		public static string IdentityClaimsToFriendlyText( IList<Tuple<string, string>> claims )
		{
			var name = claims.SingleOrDefault( claim => claim.Item1 == "name" )?.Item2;
			var email = claims.SingleOrDefault( claim => claim.Item1 == "email" )?.Item2;

			return $"{name} ({email})";
		}

		public static string TokenToFriendlyText( string jwtEncodedString )
		{
			if ( string.IsNullOrEmpty( jwtEncodedString ) )
				return "";

			try
			{
				var decodedToken = DecodeSecurityToken( jwtEncodedString );
				if ( decodedToken != null )
				{
					return IdentityClaimsToFriendlyText( decodedToken.Claims.Select( claim => Tuple.Create( claim.Type, claim.Value ) ).ToList() );
				}
			}
			catch
			{
				// ignored
			}
			return "";
		}

		public static string TokenToMailAddress( string jwtEncodedString )
		{
			if( string.IsNullOrEmpty( jwtEncodedString ) )
				return "";

			try
			{
				var decodedToken = DecodeSecurityToken( jwtEncodedString );
				if( decodedToken != null )
				{
					return decodedToken.Claims.SingleOrDefault( c => string.Equals( c.Type, "email" ) )?.Value;
				}
			}
			catch
			{
				// ignored
			}
			return "";
		}

		private static string GetInstanceUrl( string databaseUrl )
		{
			if( databaseUrl == null )
				throw new ArgumentNullException( nameof( databaseUrl ) );

			return databaseUrl.Replace( "/DataServiceSoap", "" );
		}

		private static OAuthTokenCredential TryGetCurrentOAuthToken( string instanceUrl, ref string refreshToken )
		{
			OAuthTokenCredential result;
			// if access token is still valid (5min margin to allow for clock skew), just return it from the cache
			if( _AccessTokenCache.TryGetValue( instanceUrl, out result ) )
			{
				if( string.IsNullOrEmpty( refreshToken ) )
				{
					refreshToken = result.RefreshToken;
				}

				if( result.AccessTokenExpiration.AddMinutes( -5 ) > DateTime.UtcNow )
				{
					return result;
				}
			}

			return null;
		}

		private static async Task<OAuthTokenCredential> TryGetOAuthTokenFromRefreshTokenAsync( TokenClient tokenClient, string authority, string refreshToken )
		{
			// when a refresh token is present try to use it to acquire a new access token
			if( !string.IsNullOrEmpty( refreshToken ) )
			{
				var tokenResponse = await tokenClient.RequestRefreshTokenAsync( refreshToken ).ConfigureAwait( false );

				if( !tokenResponse.IsError )
				{
#if NETSTANDARD2_0
                    // TODO: discover userinfo endpoint via ".well-known/openid-configuration"
                    var infoClient = new UserInfoClient(new Uri(authority + "/connect/userinfo").ToString());
                    var userInfo = await infoClient.GetAsync(tokenResponse.AccessToken).ConfigureAwait(false);
				    IEnumerable<Tuple<string, string>> castedClaims = TransformIt(userInfo.Claims);
				    return OAuthTokenCredential.CreateWithClaims(castedClaims, tokenResponse.AccessToken, DateTime.UtcNow + TimeSpan.FromSeconds(tokenResponse.ExpiresIn), tokenResponse.RefreshToken);
#else
                    var infoClient = new UserInfoClient( new Uri( authority + "/connect/userinfo" ), tokenResponse.AccessToken );
					var userInfo = await infoClient.GetAsync().ConfigureAwait( false );
                    return OAuthTokenCredential.CreateWithClaims( userInfo.Claims, tokenResponse.AccessToken, DateTime.UtcNow + TimeSpan.FromSeconds( tokenResponse.ExpiresIn ), tokenResponse.RefreshToken );
#endif

                }
			}

			return null;
		}

#if NETSTANDARD2_0
	    private static IEnumerable<Tuple<string, string>> TransformIt(IEnumerable<Claim> claims)
	    {
            //TODO has to be checked :)
	        var tupleList = new List<Tuple<string, string>>();

	        foreach (var claim in claims)
	        {
	            tupleList.Add(Tuple.Create(claim.Type,claim.Value));
	        }

	        return tupleList;

	    }
#endif

        private static async Task<OAuthTokenCredential> TryGetOAuthTokenFromAuthorizeResponseAsync( TokenClient tokenClient, CryptoNumbers cryptoNumbers, AuthorizeResponse response )
		{
			if( response != null )
			{
				// claims des IdentityToken decodieren
				var claims = DecodeSecurityToken( response.IdentityToken ).Claims.ToArray();

				// die folgenden validierungen sind notwendig, um diversen CSRF / man in the middle / etc. Angriffsszenarien zu begegnen
				// state validieren
				if( !string.Equals( cryptoNumbers.State, response.State, StringComparison.Ordinal ) )
					throw new InvalidOperationException( "invalid state value in openid service responce." );

				// nonce validieren
				if( !ValidateNonce( cryptoNumbers.Nonce, claims ) )
					throw new InvalidOperationException( "invalid nonce value in identity token." );

				// c_hash validieren
				if( !ValidateCodeHash( response.Code, claims ) )
					throw new InvalidOperationException( "invalid c_hash value in identity token." );

				// code eintauschen gegen access token und refresh token, dabei den code verifier mitschicken, um man-in-the-middle Angriff auf authorization code zu begegnen (PKCE)
				var tokenResponse = await tokenClient.RequestAuthorizationCodeAsync(
					code: response.Code,
					redirectUri: RedirectUri,
					codeVerifier: cryptoNumbers.Verifier ).ConfigureAwait( false );

				if( tokenResponse.IsError )
					throw new InvalidOperationException( "error during request of access token using authorization code: " + tokenResponse.Error );

				return OAuthTokenCredential.CreateWithIdentityToken( tokenResponse.IdentityToken, tokenResponse.AccessToken, DateTime.UtcNow + TimeSpan.FromSeconds( tokenResponse.ExpiresIn ), tokenResponse.RefreshToken );
			}

			return null;
		}

		private static void StoreToken( string instanceUrl, [NotNull] OAuthTokenCredential result )
		{
			if( result == null ) throw new ArgumentNullException( nameof( result ) );

			var newCache = new Dictionary<string, OAuthTokenCredential>( _AccessTokenCache );
			newCache[ instanceUrl ] = result;
			_AccessTokenCache = newCache;

			SerializeTokenCache();
		}

		private static string CreateOAuthStartUrl( string authority, CryptoNumbers cryptoNumbers )
		{
#if NETSTANDARD2_0
// FUTURE: discover authorize endpoint via ".well-known/openid-configuration"
			var request = new RequestUrl( authority + "/connect/authorize" );
			return request.CreateAuthorizeUrl(
				clientId: ClientId,
				responseType: "id_token code",
				responseMode: "form_post",
				scope: "openid profile email offline_access piweb",
				redirectUri: RedirectUri,
				state: cryptoNumbers.State,
				nonce: cryptoNumbers.Nonce,
				codeChallenge: cryptoNumbers.Challenge,
				codeChallengeMethod: OidcConstants.CodeChallengeMethods.Sha256 );
#else
            // FUTURE: discover authorize endpoint via ".well-known/openid-configuration"
		    var request = new AuthorizeRequest(authority + "/connect/authorize");
		    return request.CreateAuthorizeUrl(
                clientId: ClientId,
				responseType: "id_token code",
				responseMode: "form_post",
				scope: "openid profile email offline_access piweb",
				redirectUri: RedirectUri,
				state: cryptoNumbers.State,
				nonce: cryptoNumbers.Nonce,
				codeChallenge: cryptoNumbers.Challenge,
				codeChallengeMethod: OidcConstants.CodeChallengeMethods.Sha256 );
#endif
		}

		private static async Task<string> CreateAuthorityAsync( string instanceUrl )
		{
			var authority = await DiscoverOpenIdAuthorityAsync( instanceUrl ).ConfigureAwait( false );
			if( authority == null )
				throw new InvalidOperationException( "cannot detect OpenID authority from resource URL." );

			return authority;
		}

		private static TokenClient CreateTokenClient( string authority )
		{
			// TODO: discover token endpoint via ".well-known/openid-configuration"
			return new TokenClient( authority + "/connect/token", ClientId, ClientSecret );
		}

		public static async Task<OAuthTokenCredential> GetAuthenticationInformationForDatabaseUrlAsync( string databaseUrl, string refreshToken = null, Func<OAuthRequest, Task<OAuthResponse>> requestCallbackAsync = null )
		{
			var instanceUrl = GetInstanceUrl( databaseUrl );

			// deserialize token cache from file (only if file exists and has changed since last deserialization)
			DeserializeTokenCache();

			var result = TryGetCurrentOAuthToken( instanceUrl, ref refreshToken );
			if( result != null )
			{
				return result;
			}

			var authority = await CreateAuthorityAsync( instanceUrl ).ConfigureAwait(false);
			var tokenClient = CreateTokenClient( authority );

			result = await TryGetOAuthTokenFromRefreshTokenAsync( tokenClient, authority, refreshToken ).ConfigureAwait( false );
			if( result != null )
			{
				StoreToken( instanceUrl, result );
				return result;
			}

			// no refresh token or refresh token expired, do the full auth cycle eventually involving a password prompt
			if( requestCallbackAsync != null )
			{
				var cryptoNumbers = new CryptoNumbers();
				var startUrl = CreateOAuthStartUrl( authority, cryptoNumbers );

				var request = new OAuthRequest( startUrl, RedirectUri );
				var response = ( await requestCallbackAsync( request ).ConfigureAwait( false ) )?.ToAuthorizeResponse();
				if( response != null )
				{
					result = await TryGetOAuthTokenFromAuthorizeResponseAsync( tokenClient, cryptoNumbers, response ).ConfigureAwait( false );
					if( result != null )
					{
						StoreToken( instanceUrl, result );
						return result;
					}
				}
			}

			return null;
		}

		public static OAuthTokenCredential GetAuthenticationInformationForDatabaseUrl( string databaseUrl, string refreshToken = null, Func<OAuthRequest, OAuthResponse> requestCallback = null )
		{
			var instanceUrl = GetInstanceUrl( databaseUrl );

			// deserialize token cache from file (only if file exists and has changed since last deserialization)
			DeserializeTokenCache();

			var result = TryGetCurrentOAuthToken( instanceUrl, ref refreshToken );
			if( result != null )
			{
				return result;
			}

			var authority = CreateAuthorityAsync( instanceUrl )
				.ConfigureAwait(false)
				.GetAwaiter()
				.GetResult();

			var tokenClient = CreateTokenClient( authority );

			result = TryGetOAuthTokenFromRefreshTokenAsync( tokenClient, authority, refreshToken )
				.ConfigureAwait( false )
				.GetAwaiter()
				.GetResult();

			if( result != null )
			{
				StoreToken( instanceUrl, result );
				return result;
			}

			// no refresh token or refresh token expired, do the full auth cycle eventually involving a password prompt
			if( requestCallback != null )
			{
				var cryptoNumbers = new CryptoNumbers();
				var startUrl = CreateOAuthStartUrl( authority, cryptoNumbers );

				var request = new OAuthRequest( startUrl, RedirectUri );
				var response = requestCallback( request )?.ToAuthorizeResponse();
				if( response != null )
				{
					result = TryGetOAuthTokenFromAuthorizeResponseAsync( tokenClient, cryptoNumbers, response )
						.ConfigureAwait( false )
						.GetAwaiter()
						.GetResult();
					if( result != null )
					{
						StoreToken( instanceUrl, result );
						return result;
					}
				}
			}

			return null;
		}


		/// <summary>
		/// get openid authority info from service without any authentication
		/// FUTURE: use the WebFinger method discovery method described in OpenID connect specification section 2
		/// <seealso>
		///     <cref>https://openid.net/specs/openid-connect-discovery-1_0.html#IssuerDiscovery</cref>
		/// </seealso>
		/// </summary>
		private static async Task<string> DiscoverOpenIdAuthorityAsync( string resourceUrl )
		{
			var oauthServiceRest = new OAuthServiceRestClient( new Uri( resourceUrl ) )
			{
				UseDefaultWebProxy = true
			};
			var tokenInfo = await oauthServiceRest.GetOAuthTokenInformation().ConfigureAwait( false );

			return tokenInfo.OpenIdAuthority;
		}

		public static bool ValidateNonce( string expectedNonce, IEnumerable<Claim> tokenClaims )
		{
			var tokenNonce = tokenClaims.FirstOrDefault( c => c.Type == JwtClaimTypes.Nonce );

			return tokenNonce != null && string.Equals( tokenNonce.Value, expectedNonce, StringComparison.Ordinal );
		}

		private static bool ValidateCodeHash( string authorizationCode, IEnumerable<Claim> tokenClaims )
		{
			var cHash = tokenClaims.FirstOrDefault( c => c.Type == JwtClaimTypes.AuthorizationCodeHash );

			using( var sha = SHA256.Create() )
			{
				var codeHash = sha.ComputeHash( System.Text.Encoding.ASCII.GetBytes( authorizationCode ) );
				var leftBytes = new byte[ 16 ];
				Array.Copy( codeHash, leftBytes, 16 );

				var codeHashB64 = Base64Url.Encode( leftBytes );

				return string.Equals( cHash.Value, codeHashB64, StringComparison.Ordinal );
			}
		}

		public static void SerializeTokenCache()
		{
			if( !Environment.UserInteractive )
				return;

			if( !Directory.Exists( _CacheFileDirectory ) )
				Directory.CreateDirectory( _CacheFileDirectory );

			var serialized = JsonConvert.SerializeObject( _AccessTokenCache );
			var bytes = System.Text.Encoding.UTF8.GetBytes( serialized );
			bytes = ProtectedData.Protect( bytes, null, DataProtectionScope.CurrentUser );

			using( var stream = WaitForFileStream( _CacheFilePath, FileMode.Create, FileAccess.Write, FileShare.None ) )
			{
				stream.Write( bytes, 0, bytes.Length );
			}

			var fileInfo = new FileInfo( _CacheFilePath );
			_FileLastWriteTime = fileInfo.LastWriteTimeUtc;
			_FileLength = fileInfo.Length;
		}

		private static void DeserializeTokenCache()
		{
			if( !Environment.UserInteractive )
				return;

			var fileInfo = new FileInfo( _CacheFilePath );
			if( fileInfo.Exists )
			{
				var fileChanged = ( _FileLastWriteTime != fileInfo.LastWriteTimeUtc ) || ( _FileLength != fileInfo.Length );

				if( fileChanged )
				{
					using( var memStream = new MemoryStream() )
					{
						using( var stream = WaitForFileStream( _CacheFilePath, FileMode.Open, FileAccess.Read, FileShare.Read ) )
						{
							stream.CopyTo( memStream );
						}
						var bytes = ProtectedData.Unprotect( memStream.ToArray(), null, DataProtectionScope.CurrentUser );
						var serialized = System.Text.Encoding.UTF8.GetString( bytes );

						_AccessTokenCache = JsonConvert.DeserializeObject<Dictionary<string, OAuthTokenCredential>>( serialized );

						_FileLastWriteTime = fileInfo.LastWriteTimeUtc;
						_FileLength = fileInfo.Length;
					}
				}
			}
			else
			{
				_AccessTokenCache = new Dictionary<string, OAuthTokenCredential>();
				_FileLastWriteTime = default( DateTime );
				_FileLength = 0;
			}
		}

		/// <summary>
		/// Inspired by https://stackoverflow.com/a/3677960
		/// </summary>
		private static FileStream WaitForFileStream( string path, FileMode mode, FileAccess access, FileShare share )
		{
			int numTries = 10;
			int i = 0;
			while( true )
			{
				FileStream fileStream = null;
				try
				{
					fileStream = new FileStream( path, mode, access, share );
					return fileStream;
				}
				catch( IOException )
				{
					if( fileStream != null )
						fileStream.Dispose();

					if( i >= numTries )
						throw;
					else
						System.Threading.Thread.Sleep( 50 );

					i += 1;
				}
			}
		}

		public static void ClearAuthenticationInformationForDatabaseUrl( string databaseUrl )
		{
			var instanceUrl = databaseUrl.Replace( "/DataServiceSoap", "" );

			var newCache = new Dictionary<string, OAuthTokenCredential>( _AccessTokenCache );
			newCache.Remove( instanceUrl );
			_AccessTokenCache = newCache;

			// FUTURE: call endsession endpoint of identity server
			// https://identityserver.github.io/Documentation/docsv2/endpoints/endSession.html

			SerializeTokenCache();
		}



		private class CryptoNumbers
		{
			public CryptoNumbers()
			{
				Nonce = CryptoRandom.CreateUniqueId(); // only default length of 16 as this is included in the access token which should small
				State = CryptoRandom.CreateUniqueId( 32 );
				Verifier = CryptoRandom.CreateUniqueId( 32 );
#if NETSTANDARD2_0
		Challenge = Verifier.ToSha256();
#else
                Challenge = Verifier.ToCodeChallenge();
#endif

            }

			public string Nonce { get; }
			public string State { get; }
			public string Verifier { get; }
			public string Challenge { get; }
		}
	}
}