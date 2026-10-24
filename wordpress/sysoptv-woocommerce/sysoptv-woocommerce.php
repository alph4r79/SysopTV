<?php
/**
 * Plugin Name:          SysopTV for WooCommerce
 * Plugin URI:           https://github.com/alph4r79/SysopTV
 * Description:          Shows orders and revenue of your WooCommerce shop on a SysopTV desk cube. Provides a small, key protected REST endpoint with totals only, no customer data.
 * Version:              0.1.0
 * Author:               Christoph Scheel
 * Author URI:           https://github.com/alph4r79
 * License:              GPL-3.0-or-later
 * License URI:          https://www.gnu.org/licenses/gpl-3.0.html
 * Requires at least:    6.0
 * Requires PHP:         7.4
 * WC requires at least: 7.1
 * Text Domain:          sysoptv-woocommerce
 * Domain Path:          /languages
 * Update URI:           false
 *
 * SysopTV, Copyright (C) 2026 Christoph Scheel
 * Licensed under the GNU General Public License v3.0 or later, see LICENSE.
 *
 * Orders are only read through the WooCommerce API (wc_get_orders and
 * WC_Order), never with SQL on posts or order tables. This keeps the plugin
 * working with both order storages, the classic posts table and the high
 * performance order storage (HPOS, custom order tables).
 */

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

define( 'SYSOPTV_WC_VERSION', '0.1.0' );
define( 'SYSOPTV_WC_FILE', __FILE__ );
define( 'SYSOPTV_WC_DIR', plugin_dir_path( __FILE__ ) );

require_once SYSOPTV_WC_DIR . 'includes/class-sysoptv-wc-key.php';
require_once SYSOPTV_WC_DIR . 'includes/class-sysoptv-wc-stats.php';
require_once SYSOPTV_WC_DIR . 'includes/class-sysoptv-wc-api.php';
require_once SYSOPTV_WC_DIR . 'includes/class-sysoptv-wc-settings.php';

/**
 * Declares compatibility with the high performance order storage.
 */
add_action(
	'before_woocommerce_init',
	static function () {
		if ( class_exists( '\Automattic\WooCommerce\Utilities\FeaturesUtil' ) ) {
			\Automattic\WooCommerce\Utilities\FeaturesUtil::declare_compatibility( 'custom_order_tables', SYSOPTV_WC_FILE, true );
		}
	}
);

add_action(
	'init',
	static function () {
		load_plugin_textdomain( 'sysoptv-woocommerce', false, dirname( plugin_basename( SYSOPTV_WC_FILE ) ) . '/languages' );
	}
);

add_action(
	'plugins_loaded',
	static function () {
		if ( ! class_exists( 'WooCommerce' ) ) {
			// Without WooCommerce only a hint, no error.
			add_action( 'admin_notices', 'sysoptv_wc_missing_notice' );
			return;
		}
		SysopTV_WC_Api::register();
		SysopTV_WC_Settings::register();
	}
);

/**
 * Admin notice when WooCommerce is not active.
 */
function sysoptv_wc_missing_notice() {
	if ( ! current_user_can( 'activate_plugins' ) ) {
		return;
	}
	echo '<div class="notice notice-warning"><p>' . esc_html__( 'SysopTV for WooCommerce needs WooCommerce. Please install and activate WooCommerce first.', 'sysoptv-woocommerce' ) . '</p></div>';
}
