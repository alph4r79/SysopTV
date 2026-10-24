<?php
/**
 * The access key: created at random, only its HMAC is stored.
 *
 * SysopTV, Copyright (C) 2026 Christoph Scheel
 * Licensed under the GNU General Public License v3.0 or later, see LICENSE.
 */

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Key handling and the options of the plugin.
 */
class SysopTV_WC_Key {

	const OPTION_HASH = 'sysoptv_wc_key_hash';
	const OPTION_LAST = 'sysoptv_wc_last_success';

	/**
	 * Creates a new key, stores its hash and returns the key itself, which is
	 * shown exactly once and never stored.
	 *
	 * @return string 64 hex characters (32 random bytes).
	 */
	public static function create() {
		$key = bin2hex( random_bytes( 32 ) );
		update_option( self::OPTION_HASH, self::hash( $key ), false );
		return $key;
	}

	/**
	 * Removes the key, the endpoint answers 401 afterwards.
	 */
	public static function revoke() {
		delete_option( self::OPTION_HASH );
	}

	/**
	 * Whether a key is set.
	 *
	 * @return bool
	 */
	public static function exists() {
		return '' !== (string) get_option( self::OPTION_HASH, '' );
	}

	/**
	 * Compares a key from a request with the stored hash in constant time.
	 *
	 * @param string $key Key from the header X-SysopTV-Key.
	 * @return bool
	 */
	public static function matches( $key ) {
		$stored = (string) get_option( self::OPTION_HASH, '' );
		if ( '' === $stored || '' === $key ) {
			return false;
		}
		return hash_equals( $stored, self::hash( $key ) );
	}

	/**
	 * HMAC of the key with the salt of the site.
	 *
	 * @param string $key Key.
	 * @return string
	 */
	private static function hash( $key ) {
		return hash_hmac( 'sha256', $key, wp_salt( 'auth' ) );
	}

	/**
	 * Remembers the time of a successful request.
	 */
	public static function touch() {
		update_option( self::OPTION_LAST, time(), false );
	}

	/**
	 * Time of the last successful request, 0 if never.
	 *
	 * @return int Unix seconds.
	 */
	public static function last_success() {
		return (int) get_option( self::OPTION_LAST, 0 );
	}
}
