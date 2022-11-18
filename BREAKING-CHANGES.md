# Breaking Changes

Any breaking changes will require either a change in the code that uses the library or describes a change in behavior.

## v2.0.0: November 2022

The member names reported back for IEnumerable collection properties will now return the index inside of square brackets as suggested by issue #24.

- OLD: `Items.SimpleA.BoolC`
- NEW: `Items[1].SimpleA.BoolC`

This will impact any code that was parsing the member name string if IEnumerable properties were validated.
