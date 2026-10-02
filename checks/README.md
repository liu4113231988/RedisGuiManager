Run from the repository root:

    dotnet run --project checks/RegressionChecks.csproj -f net10.0-windows

The checks cover credential encryption, old plaintext configuration compatibility,
connection draft isolation, binary edit protection, damaged-file preservation,
ACL settings, read-only UI, dirty tracking, pagination/cancellation, safe import
validation, stream page boundaries and editor database binding.
To also test binary snapshot imports, TTL, scanning/pagination, Redis mutations and
atomic save conflicts, set REDIS_CHECK_ENDPOINT to a Redis test server. Those checks
create and remove only their own randomly named keys in DB 0; they never flush a database.
Full live coverage requires Stream commands and enabled Lua scripting. Missing server
capabilities are reported as blocked checks and cause a failing exit code.

Saved passwords are protected for the current Windows user. Old plaintext
connections are read and encrypted on the next save. Moving encrypted connection
files to another Windows user/machine requires re-entering the passwords; existing
backup files are not rewritten. Data exports use Redis DUMP in addition to the
legacy text fields, so restoring them requires a compatible Redis version.
