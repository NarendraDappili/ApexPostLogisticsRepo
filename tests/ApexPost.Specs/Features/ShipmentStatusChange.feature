# Copilot-assisted: drafted by GitHub Copilot from Prompt 2, then reviewed and changed by hand.
# What was kept, changed or rejected, and why, is in COPILOT-LOG.md, section 3.
Feature: Changing a shipment status at head office
  A shipment progresses through its delivery journey one step at a time.
  Only staff with the Courier role may change its status.

  Runs at API level, like ShipmentCreation.feature: the status rules and the role rule both take part only there.

  Background:
    Given courier "CR-001" is signed in at head office

  Scenario Outline: A courier moves a shipment to the next status
    Given shipment "<trackingNumber>" is at status "<currentStatus>"
    When the courier changes shipment "<trackingNumber>" to status "<nextStatus>"
    Then shipment "<trackingNumber>" has status "<nextStatus>"

    Examples:
      | trackingNumber | currentStatus  | nextStatus     |
      | APX-53000001   | Created        | PickedUp       |
      | APX-53000002   | PickedUp       | InTransit      |
      | APX-53000003   | InTransit      | OutForDelivery |
      | APX-53000004   | OutForDelivery | Delivered      |
      | APX-53000005   | OutForDelivery | Failed         |

  Scenario Outline: A courier cannot skip or reverse a shipment status
    Given shipment "<trackingNumber>" is at status "<currentStatus>"
    When the courier changes shipment "<trackingNumber>" to status "<attemptedStatus>"
    Then the change is refused and the next allowed status is "<allowedStatus>"
    And shipment "<trackingNumber>" remains at status "<currentStatus>"

    Examples:
      | trackingNumber | currentStatus  | attemptedStatus | allowedStatus        |
      | APX-53000006   | Created        | InTransit       | PickedUp             |
      | APX-53000007   | InTransit      | Created         | OutForDelivery       |
      | APX-53000008   | OutForDelivery | PickedUp        | Delivered or Failed  |

  Scenario Outline: A final shipment status cannot be changed
    Given shipment "<trackingNumber>" is at status "<finalStatus>"
    When the courier changes shipment "<trackingNumber>" to status "InTransit"
    Then the change is refused because the status is final
    And shipment "<trackingNumber>" remains at status "<finalStatus>"

    Examples:
      | trackingNumber | finalStatus |
      | APX-53000009   | Delivered   |
      | APX-53000010   | Failed      |

  Scenario: Staff without the Courier role cannot change a shipment status
    Given shipment "APX-53000011" is at status "Created"
    And an operations administrator is signed in at head office
    When the administrator changes shipment "APX-53000011" to status "PickedUp"
    Then the status change is refused because the staff member is not a courier
    And shipment "APX-53000011" remains at status "Created"
