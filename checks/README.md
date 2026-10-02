Run from the repository root:

    dotnet run --project checks/RegressionChecks.csproj -f net10.0-windows

The checks cover credential encryption, old plaintext configuration compatibility,
connection draft isolation, binary edit protection and damaged-file preservation.
To also test Redis mutations, set REDIS_CHECK_ENDPOINT to a disposable Redis test
server. Those checks create and remove only their own randomly named key in DB 0.

Saved passwords are protected for the current Windows user. Old plaintext
connections are read and encrypted on the next save. Moving encrypted connection
files to another Windows user/machine requires re-entering the passwords; existing
backup files are not rewritten. Data exports use Redis DUMP in addition to the
legacy text fields, so restoring them requires a compatible Redis version.
