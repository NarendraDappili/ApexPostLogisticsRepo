Feature: Registering a shipment at head office
  Head office registers every parcel it will track. A registered shipment can be looked up by its tracking number
  straight away. A tracking number belongs to one parcel only, the weight must be one the network can carry, and
  only head office staff may register shipments.

  Runs at API level: the real tracking API in memory, over its own SQL Server database. Only there do the request
  validation, the unique tracking number and the sign-in rules all take part (TESTING-STRATEGY.md §3, §9).

  Background:
    Given an operations administrator is signed in at head office

  Scenario: A new shipment is registered and can be looked up by its tracking number
    When the administrator registers shipment "APX-52000001" for "Asha Rao" in "Pune" weighing 2.5 kg
    Then the shipment is registered with the status "Created"
    And looking up "APX-52000001" shows "Asha Rao" in "Pune" weighing 2.5 kg

  Scenario: A tracking number cannot be registered twice
    Given shipment "APX-52000002" has been registered for "Asha Rao"
    When the administrator registers shipment "APX-52000002" for "Ben Okafor" in "Leeds" weighing 1 kg
    Then the registration is refused because the tracking number is already in use
    And looking up "APX-52000002" still shows "Asha Rao"

  Scenario Outline: A shipment must weigh from 0.1 kg to 70 kg
    When the administrator registers a shipment weighing <weight> kg
    Then the registration is <outcome>

    Examples:
      | weight | outcome                       |
      | 0.09   | refused because of the weight |
      | 0.1    | accepted                      |
      | 70     | accepted                      |
      | 70.01  | refused because of the weight |

  Scenario: A courier cannot register shipments
    Given courier "CR-101" is signed in at head office
    When the courier registers shipment "APX-52000003"
    Then the registration is refused because only head office staff may register shipments
    And head office does not know "APX-52000003"
