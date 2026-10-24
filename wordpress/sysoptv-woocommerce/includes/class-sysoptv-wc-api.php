<?php
/**
 * REST endpoint GET /wp-json/sysoptv/v1/stats for the SysopTV program.
 *
 * Order of the checks: HTTPS, lock after failed keys, key, at most one
 * request per 30 seconds, then the totals from a cache of 60 seconds.
 *
 * SysopTV, Copyright (C) 2026 Christoph Scheel
 * Licensed under the GNU General Public License v3.0 or later, see LICENSE.
 */

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * The endpoint.
 */
class SysopTV_WC_Api {

	const NAMESPACE_V1   = 'sysoptv/v1';
	const ROUTE          = '/stats';
	const T_STATS        = 'sysoptv_wc_stats';
	const T_LAST         = 'sysoptv_wc_last_request';
	const T_FAIL         = 'sysoptv_wc_fail_';
	const T_LOCK         = 'sysoptv_wc_lock_';
	const MIN_GAP        = 30;
	const CACHE_SECONDS  = 60;
	const MAX_FAILS      = 10;
	const FAIL_WINDOW    = 600;
	const LOCK_SECONDS   = 900;

	/**
	 * Hooks the route in.
	 */
	public static function register() {
		add_action( 'rest_api_init', array( __CLASS__, 'routes' ) );
	}

	/**
	 * Registers the route. The permission check is done in the callback,
	 * because it needs its own answers (403, 429 with Retry-After).
	 */
	public static function routes() {
		register_rest_route(
			self::NAMESPACE_V1,
			self::ROUTE,
			array(
				'methods'             => 'GET',
				'callback'            => array( __CLASS__, 'stats' ),
				'permission_callback' => '__return_true',
			)
		);
	}

	/**
	 * Address of the endpoint, for the settings page.
	 *
	 * @return string
	 */
	public static function url() {
		return rest_url( self::NAMESPACE_V1 . self::ROUTE );
	}

	/**
	 * Whether a request without HTTPS is allowed: only on a local site.
	 *
	 * @return bool
	 */
	private static function http_allowed() {
		return function_exists( 'wp_get_environment_type' ) && 'local' === wp_get_environment_type();
	}

	/**
	 * Name of the transients for the client, from a hash of its address. The
	 * address itself is never stored or sent.
	 *
	 * @return string
	 */
	private static function client() {
		$ip = isset( $_SERVER['REMOTE_ADDR'] ) ? sanitize_text_field( wp_unslash( $_SERVER['REMOTE_ADDR'] ) ) : '';
		return substr( wp_hash( $ip ), 0, 16 );
	}

	/**
	 * An error answer that caches nowhere.
	 *
	 * @param int    $status  HTTP status.
	 * @param string $code    Machine readable code.
	 * @param string $message Text for people.
	 * @param int    $retry   Seconds for Retry-After, 0 for none.
	 * @return WP_REST_Response
	 */
	private static function error( $status, $code, $message, $retry = 0 ) {
		$response = new WP_REST_Response(
			array(
				'code'    => $code,
				'message' => $message,
			),
			$status
		);
		$response->header( 'Cache-Control', 'no-store' );
		if ( $retry > 0 ) {
			$response->header( 'Retry-After', (string) $retry );
		}
		return $response;
	}

	/**
	 * Seconds left of a transient that holds its end time.
	 *
	 * @param string $name Transient.
	 * @return int
	 */
	private static function seconds_left( $name ) {
		$until = (int) get_transient( $name );
		return max( 1, $until - time() );
	}

	/**
	 * GET /wp-json/sysoptv/v1/stats
	 *
	 * @param WP_REST_Request $request Request.
	 * @return WP_REST_Response
	 */
	public static function stats( $request ) {
		if ( ! is_ssl() && ! self::http_allowed() ) {
			return self::error( 403, 'sysoptv_https_required', __( 'This endpoint only answers over HTTPS. Use the https:// address of the site.', 'sysoptv-woocommerce' ) );
		}

		$client = self::client();
		if ( false !== get_transient( self::T_LOCK . $client ) ) {
			$retry = self::seconds_left( self::T_LOCK . $client );
			return self::error( 429, 'sysoptv_locked', __( 'Too many wrong keys. Access is locked for a while.', 'sysoptv-woocommerce' ), $retry );
		}

		$key = (string) $request->get_header( 'X-SysopTV-Key' );
		$key = trim( sanitize_text_field( $key ) );
		if ( ! SysopTV_WC_Key::matches( $key ) ) {
			$fails = (int) get_transient( self::T_FAIL . $client ) + 1;
			if ( $fails >= self::MAX_FAILS ) {
				delete_transient( self::T_FAIL . $client );
				set_transient( self::T_LOCK . $client, time() + self::LOCK_SECONDS, self::LOCK_SECONDS );
			} else {
				set_transient( self::T_FAIL . $client, $fails, self::FAIL_WINDOW );
			}
			return self::error( 401, 'sysoptv_bad_key', __( 'The key is missing or wrong.', 'sysoptv-woocommerce' ) );
		}
		delete_transient( self::T_FAIL . $client );

		if ( false !== get_transient( self::T_LAST ) ) {
			$retry = self::seconds_left( self::T_LAST );
			return self::error( 429, 'sysoptv_too_often', __( 'At most one request every 30 seconds.', 'sysoptv-woocommerce' ), $retry );
		}
		set_transient( self::T_LAST, time() + self::MIN_GAP, self::MIN_GAP );

		$stats = get_transient( self::T_STATS );
		if ( ! is_array( $stats ) ) {
			$stats = SysopTV_WC_Stats::compute();
			set_transient( self::T_STATS, $stats, self::CACHE_SECONDS );
		}
		SysopTV_WC_Key::touch();

		$response = new WP_REST_Response( $stats, 200 );
		$response->header( 'Cache-Control', 'no-store' );
		return $response;
	}
}
