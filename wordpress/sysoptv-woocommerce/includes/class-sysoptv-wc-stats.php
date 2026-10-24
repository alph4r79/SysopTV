<?php
/**
 * Totals for the SysopTV shop displays. Only counts and amounts, no customer
 * data: no names, addresses, e-mail, phone, IP, notes or items.
 *
 * Counted as an order: status processing, completed and on-hold.
 * Open (still to ship): processing. Shipped in the list of today: completed.
 * Not counted: cancelled, refunded, failed, pending, checkout-draft, trash.
 *
 * Revenue is the order total as WooCommerce shows it (WC_Order::get_total()):
 * gross, including shipping and taxes, after discounts, refunds not deducted.
 * This is the same as totalPriceSet in the Shopify part of the SysopTV program.
 *
 * Days, weeks (from Monday 0:00), months (from the 1st) and the year (from
 * 1 January 0:00) follow the time zone of the WordPress settings (wp_timezone()).
 *
 * The year is not counted again on every request. Finished days are kept as
 * day values in an option without autoload. Each computation counts only
 * today and the last 30 days again, because older orders rarely change their
 * status. Once a day all days of the year are counted again, a few pages of
 * 200 orders per request, so no request runs into the time limit. With a new
 * year the counting starts again.
 *
 * SysopTV, Copyright (C) 2026 Christoph Scheel
 * Licensed under the GNU General Public License v3.0 or later, see LICENSE.
 */

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Computes the totals.
 */
class SysopTV_WC_Stats {

	const COUNTED   = array( 'wc-processing', 'wc-completed', 'wc-on-hold' );
	const OPEN      = array( 'wc-processing' );
	const PAGE_SIZE = 200;
	const MAX_TODAY = 20;
	const DAYS      = 14;

	const OPTION_DAYS    = 'sysoptv_wc_days';
	const OPTION_RECOUNT = 'sysoptv_wc_recount';
	const RECENT_DAYS    = 30;
	const RECOUNT_PAGES  = 5;

	/**
	 * Builds the answer of the endpoint.
	 *
	 * @param DateTimeImmutable|null $now Current time, for tests.
	 * @return array
	 */
	public static function compute( $now = null ) {
		$tz  = wp_timezone();
		$now = $now ? $now->setTimezone( $tz ) : new DateTimeImmutable( 'now', $tz );

		$today       = $now->setTime( 0, 0, 0 );
		$yesterday   = $today->modify( '-1 day' );
		$monday      = $today->modify( '-' . ( (int) $today->format( 'N' ) - 1 ) . ' days' );
		$month_start = $today->modify( 'first day of this month' );
		$last7_start = $today->modify( '-6 days' );
		$days_start  = $today->modify( '-' . ( self::DAYS - 1 ) . ' days' );
		$recent      = $today->modify( '-' . self::RECENT_DAYS . ' days' );
		$year_start  = $today->setDate( (int) $today->format( 'Y' ), 1, 1 );
		$from        = $days_start < $month_start ? $days_start : $month_start;
		$from        = $monday < $from ? $monday : $from;
		$from        = $recent < $from ? $recent : $from;

		// Every day of the window, for the day values of the year.
		$window = array();
		for ( $d = $from; $d <= $today; $d = $d->modify( '+1 day' ) ) {
			$window[ $d->format( 'Y-m-d' ) ] = self::empty_sum();
		}

		$sums = array(
			'today'     => self::empty_sum(),
			'yesterday' => self::empty_sum(),
			'week'      => self::empty_sum(),
			'last7Days' => self::empty_sum(),
			'month'     => self::empty_sum(),
		);
		$days = array();
		for ( $i = 0; $i < self::DAYS; $i++ ) {
			$days[ $days_start->modify( '+' . $i . ' days' )->format( 'Y-m-d' ) ] = self::empty_sum();
		}
		$today_orders = array();
		$last         = null;

		// Newest first, in pages, so big shops do not run into the time limit.
		$page = 1;
		do {
			$orders = wc_get_orders(
				array(
					'type'         => 'shop_order',
					'status'       => self::COUNTED,
					'date_created' => $from->getTimestamp() . '...' . $now->getTimestamp(),
					'orderby'      => 'date',
					'order'        => 'DESC',
					'limit'        => self::PAGE_SIZE,
					'page'         => $page,
					'return'       => 'objects',
				)
			);
			foreach ( $orders as $order ) {
				$created = $order->get_date_created();
				if ( ! $created ) {
					continue;
				}
				$time  = ( new DateTimeImmutable( '@' . $created->getTimestamp() ) )->setTimezone( $tz );
				$cents = (int) round( (float) $order->get_total() * 100 );
				$day   = $time->format( 'Y-m-d' );

				if ( null === $last ) {
					$last = array( 'time' => $time, 'cents' => $cents );
				}
				self::add( $sums['today'], $time >= $today, $cents );
				self::add( $sums['yesterday'], $time >= $yesterday && $time < $today, $cents );
				self::add( $sums['week'], $time >= $monday, $cents );
				self::add( $sums['last7Days'], $time >= $last7_start, $cents );
				self::add( $sums['month'], $time >= $month_start, $cents );
				if ( isset( $window[ $day ] ) ) {
					self::add( $window[ $day ], true, $cents );
				}
				if ( isset( $days[ $day ] ) ) {
					self::add( $days[ $day ], true, $cents );
				}
				if ( $time >= $today && count( $today_orders ) < self::MAX_TODAY ) {
					$today_orders[] = array(
						'number'  => '#' . $order->get_order_number(),
						'time'    => $time->format( DATE_ATOM ),
						'amount'  => self::money( $cents ),
						'shipped' => 'completed' === $order->get_status(),
					);
				}
			}
			$page++;
		} while ( count( $orders ) === self::PAGE_SIZE );

		if ( null === $last ) {
			// No order in the window: the last one is older.
			$older = wc_get_orders(
				array(
					'type'    => 'shop_order',
					'status'  => self::COUNTED,
					'orderby' => 'date',
					'order'   => 'DESC',
					'limit'   => 1,
					'return'  => 'objects',
				)
			);
			if ( ! empty( $older ) && $older[0]->get_date_created() ) {
				$last = array(
					'time'  => ( new DateTimeImmutable( '@' . $older[0]->get_date_created()->getTimestamp() ) )->setTimezone( $tz ),
					'cents' => (int) round( (float) $older[0]->get_total() * 100 ),
				);
			}
		}

		// Open orders of any age, counted without loading them.
		$open = wc_get_orders(
			array(
				'type'     => 'shop_order',
				'status'   => self::OPEN,
				'limit'    => 1,
				'return'   => 'ids',
				'paginate' => true,
			)
		);

		$year = self::year( $now, $today, $year_start, $window, $sums['today'] );

		$day_list = array();
		foreach ( $days as $date => $sum ) {
			$day_list[] = array( 'date' => $date ) + self::out( $sum );
		}

		return array(
			'plugin'           => SYSOPTV_WC_VERSION,
			'shop'             => wp_specialchars_decode( get_bloginfo( 'name' ), ENT_QUOTES ),
			'currency'         => get_woocommerce_currency(),
			'timezone'         => wp_timezone_string(),
			'utcOffsetMinutes' => (int) ( $now->getOffset() / 60 ),
			'generatedAt'      => $now->format( DATE_ATOM ),
			'today'            => self::out( $sums['today'] ),
			'yesterday'        => self::out( $sums['yesterday'] ),
			'week'             => self::out( $sums['week'] ),
			'last7Days'        => self::out( $sums['last7Days'] ),
			'month'            => self::out( $sums['month'] ),
			'year'             => self::out( $year['sum'] ),
			'yearComplete'     => $year['complete'],
			'days'             => $day_list,
			'unshipped'        => is_object( $open ) && isset( $open->total ) ? (int) $open->total : 0,
			'lastOrder'        => $last ? array(
				'time'   => $last['time']->format( DATE_ATOM ),
				'amount' => self::money( $last['cents'] ),
			) : null,
			'todayOrders'      => $today_orders,
		);
	}

	/**
	 * Orders and revenue since 1 January from the stored day values plus the
	 * freshly counted window and today.
	 *
	 * @param DateTimeImmutable $now        Now.
	 * @param DateTimeImmutable $today      Today 0:00.
	 * @param DateTimeImmutable $year_start 1 January 0:00.
	 * @param array             $window     Freshly counted days, date => sum.
	 * @param array             $today_sum  Sum of today.
	 * @return array sum and whether all days of the year are counted.
	 */
	private static function year( $now, $today, $year_start, array $window, array $today_sum ) {
		$year_key = $today->format( 'Y' );
		$today_key = $today->format( 'Y-m-d' );
		$cache    = get_option( self::OPTION_DAYS, array() );
		if ( ! is_array( $cache ) || ! isset( $cache['year'] ) || $cache['year'] !== $year_key ) {
			// New year (or first run): the counting starts again.
			$cache = array(
				'year'      => $year_key,
				'days'      => array(),
				'complete'  => false,
				'recounted' => '',
			);
		}
		foreach ( $window as $date => $sum ) {
			if ( $date >= $year_start->format( 'Y-m-d' ) && $date < $today_key ) {
				$cache['days'][ $date ] = array( $sum['orders'], $sum['cents'] );
			}
		}
		if ( $cache['recounted'] !== $today_key ) {
			$done = self::recount_step( $now, $today, $year_start );
			if ( is_array( $done ) ) {
				// The full count is finished: it replaces the older day values,
				// the window counted just now stays as it is.
				foreach ( $done as $date => $value ) {
					if ( ! isset( $window[ $date ] ) ) {
						$cache['days'][ $date ] = $value;
					}
				}
				foreach ( array_keys( $cache['days'] ) as $date ) {
					if ( ! isset( $done[ $date ] ) && ! isset( $window[ $date ] ) ) {
						unset( $cache['days'][ $date ] );
					}
				}
				$cache['complete']  = true;
				$cache['recounted'] = $today_key;
			}
		}
		update_option( self::OPTION_DAYS, $cache, false );

		$sum = $today_sum;
		foreach ( $cache['days'] as $date => $value ) {
			if ( $date < $today_key ) {
				$sum['orders'] += (int) $value[0];
				$sum['cents']  += (int) $value[1];
			}
		}
		return array(
			'sum'      => $sum,
			'complete' => (bool) $cache['complete'],
		);
	}

	/**
	 * One step of the daily full count: at most RECOUNT_PAGES pages of 200
	 * orders from 1 January up to yesterday. Returns the day values when the
	 * count is finished, otherwise null and the progress is stored.
	 *
	 * @param DateTimeImmutable $now        Now.
	 * @param DateTimeImmutable $today      Today 0:00.
	 * @param DateTimeImmutable $year_start 1 January 0:00.
	 * @return array|null
	 */
	private static function recount_step( $now, $today, $year_start ) {
		$today_key = $today->format( 'Y-m-d' );
		$state     = get_option( self::OPTION_RECOUNT, array() );
		if ( ! is_array( $state ) || ! isset( $state['date'] ) || $state['date'] !== $today_key ) {
			$state = array(
				'date' => $today_key,
				'page' => 1,
				'days' => array(),
			);
		}
		$tz = $now->getTimezone();
		if ( $today <= $year_start ) {
			// 1 January: there are no finished days yet.
			delete_option( self::OPTION_RECOUNT );
			return array();
		}
		for ( $i = 0; $i < self::RECOUNT_PAGES; $i++ ) {
			$orders = wc_get_orders(
				array(
					'type'         => 'shop_order',
					'status'       => self::COUNTED,
					'date_created' => $year_start->getTimestamp() . '...' . ( $today->getTimestamp() - 1 ),
					'orderby'      => 'date',
					'order'        => 'ASC',
					'limit'        => self::PAGE_SIZE,
					'page'         => $state['page'],
					'return'       => 'objects',
				)
			);
			foreach ( $orders as $order ) {
				$created = $order->get_date_created();
				if ( ! $created ) {
					continue;
				}
				$date = ( new DateTimeImmutable( '@' . $created->getTimestamp() ) )->setTimezone( $tz )->format( 'Y-m-d' );
				if ( ! isset( $state['days'][ $date ] ) ) {
					$state['days'][ $date ] = array( 0, 0 );
				}
				$state['days'][ $date ][0]++;
				$state['days'][ $date ][1] += (int) round( (float) $order->get_total() * 100 );
			}
			if ( count( $orders ) < self::PAGE_SIZE ) {
				delete_option( self::OPTION_RECOUNT );
				return $state['days'];
			}
			$state['page']++;
		}
		update_option( self::OPTION_RECOUNT, $state, false );
		return null;
	}

	/**
	 * A sum of orders and cents.
	 *
	 * @return array
	 */
	private static function empty_sum() {
		return array(
			'orders' => 0,
			'cents'  => 0,
		);
	}

	/**
	 * Adds one order to a sum.
	 *
	 * @param array $sum    Sum to change.
	 * @param bool  $inside Whether the order belongs to it.
	 * @param int   $cents  Amount in cents.
	 */
	private static function add( array &$sum, $inside, $cents ) {
		if ( $inside ) {
			$sum['orders']++;
			$sum['cents'] += $cents;
		}
	}

	/**
	 * A sum as it goes out.
	 *
	 * @param array $sum Sum.
	 * @return array
	 */
	private static function out( array $sum ) {
		return array(
			'orders'  => $sum['orders'],
			'revenue' => self::money( $sum['cents'] ),
		);
	}

	/**
	 * Cents as a decimal string with a point, like "184.50".
	 *
	 * @param int $cents Amount in cents.
	 * @return string
	 */
	private static function money( $cents ) {
		$sign  = $cents < 0 ? '-' : '';
		$cents = abs( $cents );
		return $sign . intdiv( $cents, 100 ) . '.' . str_pad( (string) ( $cents % 100 ), 2, '0', STR_PAD_LEFT );
	}
}
