<?php
/**
 * Removes everything the plugin stored: the options and the transients.
 * Orders are never touched.
 *
 * SysopTV, Copyright (C) 2026 Christoph Scheel
 * Licensed under the GNU General Public License v3.0 or later, see LICENSE.
 */

if ( ! defined( 'WP_UNINSTALL_PLUGIN' ) ) {
	exit;
}

delete_option( 'sysoptv_wc_key_hash' );
delete_option( 'sysoptv_wc_last_success' );
delete_option( 'sysoptv_wc_days' );
delete_option( 'sysoptv_wc_recount' );
delete_transient( 'sysoptv_wc_stats' );
delete_transient( 'sysoptv_wc_last_request' );

// Counters and locks for wrong keys carry a hash of the client in their name.
global $wpdb;
$sysoptv_like = array(
	$wpdb->esc_like( '_transient_sysoptv_wc_' ) . '%',
	$wpdb->esc_like( '_transient_timeout_sysoptv_wc_' ) . '%',
);
foreach ( $sysoptv_like as $sysoptv_pattern ) {
	$wpdb->query( $wpdb->prepare( "DELETE FROM {$wpdb->options} WHERE option_name LIKE %s", $sysoptv_pattern ) ); // phpcs:ignore WordPress.DB.DirectDatabaseQuery -- removing own transients on uninstall.
}
