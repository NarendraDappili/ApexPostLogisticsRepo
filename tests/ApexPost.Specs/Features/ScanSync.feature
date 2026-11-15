@courier
Feature: Scans captured on a courier's device reach head office exactly once
  Couriers scan parcels on the road, often with no signal. Every scan is kept on the device first and sent to head
  office when there is a network. A scan must never be lost or stored twice. A scan head office cannot accept comes
  back to the courier with the reason.

  Runs from the courier's device to head office. The device is composed the way the app composes it: its store, its
  outbox, its automatic sync and its API client. Its requests go to the real tracking API, in memory, over SQL Server.
  The promise spans both sides, so faking either one would only prove the fake.

  Background:
    Given courier "CR-101" is signed in on their device

  Scenario: Scans recorded without a network are sent when the network returns
    Given a shipment out for delivery
    And the device has no network
    When the courier records a "Delivered" scan of that parcel at "14 Elm Street"
    And the device tries to sync
    Then the scan is waiting on the device
    And head office has no scans for that parcel
    When the network returns and the device syncs
    Then the scan is no longer waiting on the device
    And head office shows that parcel as "Delivered" with 1 scan

  Scenario: A scan that reaches head office twice is stored once
    Given a shipment out for delivery
    And the courier has recorded a "Delivered" scan of that parcel at "14 Elm Street"
    And the device has synced
    When the same scan reaches head office again
    Then head office says it already had that scan
    And head office shows that parcel as "Delivered" with 1 scan

  Scenario Outline: A scan head office cannot accept is marked as failed, with the reason
    Given <the parcel>
    When the courier records a "<scan>" scan of that parcel at "14 Elm Street" dated <when>
    And the device syncs
    Then the scan is marked as failed on the device
    And the courier is told "<reason>"

    Examples:
      | case                                | the parcel                                | scan      | when          | reason                                 |
      | it is dated in the future           | a shipment out for delivery               | Delivered | in the future | cannot be in the future                |
      | it skips a stage                    | a shipment that has just been registered  | Delivered | now           | A Delivered scan cannot be recorded    |
      | head office does not know the parcel | a parcel head office does not know        | Delivered | now           | No shipment has the tracking number    |
