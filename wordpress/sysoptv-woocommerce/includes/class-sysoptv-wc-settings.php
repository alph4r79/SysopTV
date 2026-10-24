<?php
/**
 * Settings page under WooCommerce: create, renew or revoke the key, see the
 * address of the endpoint, the last request and whether HTTPS is active.
 *
 * SysopTV, Copyright (C) 2026 Christoph Scheel
 * Licensed under the GNU General Public License v3.0 or later, see LICENSE.
 */

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * The page.
 */
class SysopTV_WC_Settings {

	const SLUG   = 'sysoptv';
	const NONCE  = 'sysoptv_wc_key';
	const CAPABILITY = 'manage_woocommerce';

	/**
	 * Hooks the menu entry in.
	 */
	public static function register() {
		add_action( 'admin_menu', array( __CLASS__, 'menu' ) );
	}

	/**
	 * Submenu of WooCommerce.
	 */
	public static function menu() {
		add_submenu_page(
			'woocommerce',
			__( 'SysopTV', 'sysoptv-woocommerce' ),
			__( 'SysopTV', 'sysoptv-woocommerce' ),
			self::CAPABILITY,
			self::SLUG,
			array( __CLASS__, 'page' )
		);
	}

	/**
	 * Handles a button and returns a new key to show once, or an empty string.
	 *
	 * @param string $notice Set to a message for the page.
	 * @return string
	 */
	private static function handle( &$notice ) {
		if ( ! isset( $_POST['sysoptv_action'] ) ) {
			return '';
		}
		check_admin_referer( self::NONCE );
		if ( ! current_user_can( self::CAPABILITY ) ) {
			wp_die( esc_html__( 'You are not allowed to do this.', 'sysoptv-woocommerce' ) );
		}
		$action = sanitize_key( wp_unslash( $_POST['sysoptv_action'] ) );
		if ( 'create' === $action ) {
			return SysopTV_WC_Key::create();
		}
		if ( 'revoke' === $action ) {
			SysopTV_WC_Key::revoke();
			delete_transient( SysopTV_WC_Api::T_STATS );
			$notice = __( 'Access revoked. The SysopTV program can no longer read the shop until you create a new key.', 'sysoptv-woocommerce' );
		}
		return '';
	}

	/**
	 * One button in its own form with nonce.
	 *
	 * @param string $action  create or revoke.
	 * @param string $label   Text of the button.
	 * @param string $confirm Question before sending, empty for none.
	 * @param string $class   CSS class of the button.
	 */
	private static function button( $action, $label, $confirm, $class ) {
		echo '<form method="post" style="display:inline-block;margin-right:8px">';
		wp_nonce_field( self::NONCE );
		echo '<input type="hidden" name="sysoptv_action" value="' . esc_attr( $action ) . '">';
		$onclick = '' !== $confirm ? ' onclick="return confirm(\'' . esc_js( $confirm ) . '\')"' : '';
		echo '<button type="submit" class="' . esc_attr( $class ) . '"' . $onclick . '>' . esc_html( $label ) . '</button>'; // phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped -- $onclick is built from esc_js.
		echo '</form>';
	}

	/**
	 * The page itself.
	 */
	public static function page() {
		if ( ! current_user_can( self::CAPABILITY ) ) {
			wp_die( esc_html__( 'You are not allowed to see this page.', 'sysoptv-woocommerce' ) );
		}
		$notice = '';
		$new    = self::handle( $notice );
		$last   = SysopTV_WC_Key::last_success();
		$https  = function_exists( 'wp_is_using_https' ) ? wp_is_using_https() : is_ssl();

		echo '<div class="wrap"><h1>' . esc_html__( 'SysopTV', 'sysoptv-woocommerce' ) . '</h1>';
		echo '<p>' . esc_html__( 'Shows orders and revenue of this shop on a SysopTV desk cube. The SysopTV program on your PC reads the totals from a small endpoint protected by a key.', 'sysoptv-woocommerce' ) . '</p>';

		if ( '' !== $notice ) {
			echo '<div class="notice notice-success"><p>' . esc_html( $notice ) . '</p></div>';
		}
		if ( '' !== $new ) {
			echo '<div class="notice notice-warning"><p><strong>' . esc_html__( 'Copy this key into the SysopTV program now. It is shown only this once and cannot be shown again.', 'sysoptv-woocommerce' ) . '</strong></p>';
			echo '<p><input type="text" readonly class="large-text code" value="' . esc_attr( $new ) . '" onfocus="this.select()"></p></div>';
		}

		echo '<h2>' . esc_html__( 'Access key', 'sysoptv-woocommerce' ) . '</h2>';
		if ( SysopTV_WC_Key::exists() ) {
			echo '<p>' . esc_html__( 'A key is set. Only a hash of it is stored on this site.', 'sysoptv-woocommerce' ) . '</p><p>';
			self::button( 'create', __( 'Create new key', 'sysoptv-woocommerce' ), __( 'The old key stops working at once. Continue?', 'sysoptv-woocommerce' ), 'button' );
			self::button( 'revoke', __( 'Revoke access', 'sysoptv-woocommerce' ), __( 'The SysopTV program can no longer read the shop. Continue?', 'sysoptv-woocommerce' ), 'button button-link-delete' );
			echo '</p>';
		} else {
			echo '<p>' . esc_html__( 'No key yet. The endpoint answers no request without a key.', 'sysoptv-woocommerce' ) . '</p><p>';
			self::button( 'create', __( 'Create key', 'sysoptv-woocommerce' ), '', 'button button-primary' );
			echo '</p>';
		}

		echo '<h2>' . esc_html__( 'Status', 'sysoptv-woocommerce' ) . '</h2><table class="form-table" role="presentation"><tbody>';
		echo '<tr><th scope="row">' . esc_html__( 'Address of the endpoint', 'sysoptv-woocommerce' ) . '</th><td><input type="text" readonly class="large-text code" value="' . esc_attr( SysopTV_WC_Api::url() ) . '" onfocus="this.select()"><p class="description">' . esc_html__( 'In the SysopTV program the address of the website is enough, for example https://shop.example.com.', 'sysoptv-woocommerce' ) . '</p></td></tr>';
		echo '<tr><th scope="row">' . esc_html__( 'Last successful request', 'sysoptv-woocommerce' ) . '</th><td>' . esc_html( $last > 0 ? wp_date( get_option( 'date_format' ) . ' ' . get_option( 'time_format' ), $last ) : __( 'never', 'sysoptv-woocommerce' ) ) . '</td></tr>';
		echo '<tr><th scope="row">' . esc_html__( 'HTTPS', 'sysoptv-woocommerce' ) . '</th><td>' . esc_html( $https ? __( 'active', 'sysoptv-woocommerce' ) : __( 'not active: the endpoint only answers over HTTPS', 'sysoptv-woocommerce' ) ) . '</td></tr>';
		echo '</tbody></table>';

		echo '<h2>' . esc_html__( 'Which data leaves the shop', 'sysoptv-woocommerce' ) . '</h2>';
		echo '<p>' . esc_html__( 'Shop name, currency, time zone, number of orders and revenue for today, yesterday, this week, this month, this year, the last 7 days and each of the last 14 days, the number of orders to ship, time and amount of the last order, and for today\'s orders (at most 20) the order number, time, amount and whether it is shipped.', 'sysoptv-woocommerce' ) . '</p>';
		echo '<p><strong>' . esc_html__( 'No customer data: no names, addresses, e-mail addresses, phone numbers, IP addresses, notes or items.', 'sysoptv-woocommerce' ) . '</strong></p>';
		echo '</div>';
	}
}
