# StudentExchangeBck developer guide

StudentExchangeBck is an ASP.NET Core API for student accounts and school selection.
It supports registration, login, reading and updating the current user's profile,
and listing schools. It uses a local SQLite database; no separate database server
is required.

This guide describes the current implementation. Examples use disposable local
credentials and data.

## Start here

1. Install the **.NET 9 SDK** and Git. Use `dotnet --info` to check your SDK.
2. Restore, build, and run the regression tests from the repository root:

   ```sh
   dotnet restore StudentExchangeBck/StudentExchangeBck.sln
   dotnet build StudentExchangeBck/StudentExchangeBck.sln
   dotnet test StudentExchangeBck/StudentExchangeBck.sln
   ```

3. Follow [Create a local development environment](#create-a-local-development-environment)
   to prepare the configuration and database required to start the API.
4. Follow [Use the REST API](#use-the-rest-api) to register an account and exercise
   the complete login/profile workflow.

Tests create their own temporary configuration and database. They can run before
application configuration is prepared and do not modify the checked-in database.

## Project map

| Location | Responsibility |
| --- | --- |
| `StudentExchangeBck/Program.cs` | Loads configuration, runs initial cleanup, starts daily maintenance, and configures ASP.NET Core. |
| `StudentExchangeBck/Controllers/Register.cs` | Registration, profile retrieval, profile updates, and field validation. |
| `StudentExchangeBck/Controllers/LogIn.cs` | Credential checks, token issuance/reuse, failed-attempt tracking, lockout, and cleanup. |
| `StudentExchangeBck/Controllers/Schools.cs` | Public school list. |
| `StudentExchangeBck/Components/AppAuthJson.cs` | Shared request credentials and application/token validation. |
| `StudentExchangeBck/Components/Env.cs` | Data-directory selection, encrypted configuration loading, and authentication policy settings. |
| `StudentExchangeBck/Components/Sql.cs` | Parameterized SQLite access, write transactions, and in-process synchronization. |
| `StudentExchangeBck/Components/DeterministicEncryption.cs` | Encryption/decryption used for configuration, emails, and passwords. |
| `StudentExchangeBck/Components/AccessToken.cs` | Cryptographically random, URL-safe access tokens. |
| `StudentExchangeBck/Components/Maitenance.cs` | Daily Mountain-time background scheduling and shutdown. The spelling matches the class/file. |
| `StudentExchangeBck/Components/Utilz.cs` | Name normalization, collection transposition, and Mountain-time conversion. |
| `StudentExchangeBck/bin_/Data.db` | Checked-in SQLite database. Keep development writes out of this file. |
| `StudentExchangeBck.Tests/` | xUnit regression tests, including an HTTP workflow test. |

Runtime package references are `Microsoft.AspNetCore.OpenApi` 9.0.12,
`Microsoft.Data.Sqlite` 10.0.12, and `Newtonsoft.Json` 13.0.4. The project file is
the source of truth for versions.

## Configuration and storage

The directory selected by `STUDENT_EXCHANGE_DATA_DIRECTORY` must contain:

- **`Data.db`**: an existing SQLite database with the required tables.
- **`.env`**: encrypted JSON containing the three settings below.

| JSON setting | Purpose |
| --- | --- |
| `_appAccess` | Shared application password supplied as `app_access` in authenticated API requests. |
| `_emailSalt` | Key material for deterministic email encryption and lookup. |
| `_passSalt` | Key material for deterministic password encryption and comparison. |

All three values must be nonempty. Configuration values are trimmed when loaded.
The file is **not a dotenv text file**: `KEY=value` lines or plaintext JSON will
not work. Its content must be the Base64 output of
`DeterministicEncryption.Encrypt(json, salt)`, using the salt expected by
`Env.GetEnv()`. `Program.cs` currently calls `GetEnv()` with its source-defined
default salt.

The data-directory variable overrides both Debug and Release defaults. Without
it, Debug resolves `bin_` relative to the process working directory, while Release
uses `/home/bin_`. Use an absolute override to make startup independent of the
working directory. Set it before starting the process; the SQLite path is
initialized once.

Keep existing email/password key material with its database. Changing either key
without migrating stored records prevents existing users from authenticating or
reading their email. A newly generated configuration belongs with a fresh local
database. Obtain existing deployment configuration through your team's secure
channel; do not commit or log it.

### Create a local development environment

The following **Bash** commands run from the repository root. They create a fresh
SQLite database and encrypted configuration under a temporary directory. They do
not copy account data or overwrite the checked-in database. The initializer is a
temporary console project referencing the API's existing encryption and SQLite
packages.

Keep this shell open: subsequent commands use its exported data-directory variable.
On Windows, run these commands in WSL, or create the same console helper in your
IDE and set the data-directory variable in your launch environment.

```sh
repo_root="$PWD"
dev_root="$(mktemp -d)"
export STUDENT_EXCHANGE_DATA_DIRECTORY="$dev_root/data"

dotnet new console --framework net9.0 --output "$dev_root/init"
dotnet add "$dev_root/init/init.csproj" reference \
  "$repo_root/StudentExchangeBck/StudentExchangeBck.csproj"

cat > "$dev_root/init/Program.cs" <<'CS'
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using StudentExchangeBck;

var directory = Env.DataDirectory;
Directory.CreateDirectory(directory);
var database = Path.Combine(directory, "Data.db");
var configuration = Path.Combine(directory, ".env");
if (File.Exists(database) || File.Exists(configuration))
    throw new InvalidOperationException("Refusing to overwrite existing development data.");

using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
       { DataSource = database }.ToString()))
{
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = """
        CREATE TABLE Schools (
            id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE);
        CREATE TABLE User (
            id INTEGER PRIMARY KEY AUTOINCREMENT, email TEXT NOT NULL UNIQUE,
            first_name TEXT NOT NULL, last_name TEXT NOT NULL, password TEXT NOT NULL,
            school_id INTEGER NOT NULL REFERENCES Schools(id), datetime TEXT);
        CREATE TABLE Access_Token (
            id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER NOT NULL REFERENCES User(id),
            token TEXT NOT NULL UNIQUE, expiry TEXT NOT NULL);
        CREATE TABLE Blocked_Users (
            id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER NOT NULL REFERENCES User(id),
            count INTEGER NOT NULL, expiry TEXT NOT NULL);
        INSERT INTO Schools (name) VALUES
            ('Metropolitan State University of Denver'),
            ('University of Colorado Denver');
        """;
    command.ExecuteNonQuery();
}

var json = JsonSerializer.Serialize(new
{
    _appAccess = "local-development-app",
    _emailSalt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
    _passSalt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
});
// This matches Env.GetEnv's current default. It is not a production secret.
File.WriteAllText(configuration, DeterministicEncryption.Encrypt(json, "tempPass!^74*"));
Console.WriteLine("Created an isolated local database and encrypted configuration.");
CS

dotnet run --project "$dev_root/init/init.csproj"
```

The helper's `local-development-app` password is for these local examples only.
The email/password keys are randomly generated and are retained in that temporary
configuration. To reuse this local database in another shell, export the same
absolute `STUDENT_EXCHANGE_DATA_DIRECTORY` value. Temporary directories may be
removed by the operating system; use a persistent private directory if you need
to retain your local accounts.

### Run and debug

From the repository root, with the data-directory variable still exported:

```sh
dotnet run --project StudentExchangeBck/StudentExchangeBck.csproj --launch-profile http
```

The HTTP profile listens on `http://localhost:5239` and sets the ASP.NET Core
environment to `Development`. For automatic rebuilds during editing:

```sh
cd StudentExchangeBck
dotnet watch run --launch-profile http
```

For local HTTPS, prepare the development certificate and use the HTTPS profile:

```sh
dotnet dev-certs https --trust
dotnet run --project StudentExchangeBck/StudentExchangeBck.csproj --launch-profile https
```

Run the second command from the repository root. Certificate trust support varies
by operating system; the HTTP profile is sufficient for the local examples. The
HTTPS profile uses `https://localhost:7000` and also binds the HTTP port. The app
includes HTTPS redirection, so use the HTTPS address when that profile is active.

In Visual Studio or another .NET debugger, open
`StudentExchangeBck/StudentExchangeBck.sln`, select the API project and a launch
profile, and provide the absolute data-directory variable in your local launch
environment. Useful first breakpoints are `Env.GetEnv`, `Register.Post`,
`LogIn.Post`, and `AppAuthJson.ValidateWithToken`.

Development exposes an OpenAPI document at `/openapi/v1.json`. There is no
configured Swagger UI. The `.http` file currently points at an Azure address and
contains incomplete requests; use the complete local examples below.

## Use the REST API

Requests and responses use JSON. Send `Content-Type: application/json` for
requests with a body. The current API uses credentials in the **JSON body**,
including for `GET /Register`; it does not implement Bearer-header or cookie
authentication.

| Method | Route | Credentials | Success |
| --- | --- | --- | --- |
| GET | `/Schools` | None | `200` with `Schools` array. |
| POST | `/Register` | `app_access` | `201` with registration confirmation and normalized email. |
| POST | `/LogIn` | `app_access`, email, password | `201` with user token and first name. |
| GET | `/Register` | `app_access`, `access_token` | `200` with current user's profile. |
| PATCH | `/Register` | `app_access`, `access_token` | `200` with update confirmation and email. |

`app_access` is the shared application password. `access_token` identifies an
individual logged-in user. Registration does not issue a token: log in afterward.
There is no logout, account deletion, password-reset, or school-management endpoint.

### 1. List schools

```sh
curl -i http://localhost:5239/Schools
```

Response body for the local seed database:

```json
{
  "Schools": [
    "Metropolitan State University of Denver",
    "University of Colorado Denver"
  ]
}
```

The `Schools` dictionary key retains its capital `S`; other response examples use
ASP.NET Core's camel-case property serialization. An empty table returns
`{"Schools":[]}`. Select a school name from this list for registration.

### 2. Register an account

```sh
curl -i http://localhost:5239/Register \
  -H 'Content-Type: application/json' \
  --data '{
    "app_access": "[[app_access]]",
    "email": "jane@example.com",
    "first_name": "jane",
    "last_name": "doe",
    "password": "Password!123",
    "school": "University of Colorado Denver"
  }'
```

Success is **201 Created**:

```json
{"message":"User added.","email":"jane@example.com"}
```

Registering the same normalized email again returns **409 Conflict**.

### 3. Log in

```sh
curl -i http://localhost:5239/LogIn \
  -H 'Content-Type: application/json' \
  --data '{
    "app_access": "[[app_access]]",
    "email": "jane@example.com",
    "password": "Password!123"
  }'
```

Success is **201 Created** (the token below is a placeholder):

```json
{
  "message": "Successfull Login.",
  "access_token": "YOUR_RETURNED_TOKEN",
  "first_name": "Jane"
}
```

Copy the returned `access_token` into subsequent profile requests. The misspelling
in the success message reflects the current response. Use status codes and
structured fields rather than matching the message text.

### 4. Read your profile

```sh
curl -i --request GET http://localhost:5239/Register \
  -H 'Content-Type: application/json' \
  --data '{
    "app_access": "[[app_access]]",
    "access_token": "YOUR_RETURNED_TOKEN"
  }'
```

Success is **200 OK**:

```json
{
  "email": "jane@example.com",
  "first_name": "Jane",
  "last_name": "Doe",
  "school": "University of Colorado Denver"
}
```

The profile response does **not** return a password. Some clients and proxies do
not support GET requests with bodies; browser `fetch` also disallows them. This
endpoint currently requires one. Query parameters or an `Authorization` header
are not substitutes; clients must use a transport supporting this contract, or
the API contract must be deliberately revised.

### 5. Update selected profile fields

```sh
curl -i --request PATCH http://localhost:5239/Register \
  -H 'Content-Type: application/json' \
  --data '{
    "app_access": "local-development-app",
    "access_token": "YOUR_RETURNED_TOKEN",
    "first_name": "alice"
  }'
```

Success is **200 OK**:

```json
{"message":"Successfully updated: first_name.","email":"jane@example.com"}
```

Editable fields are `email`, `first_name`, `last_name`, `password`, and `school`.
Omitted or JSON `null` fields retain their existing values. An explicitly empty or
whitespace-only field is invalid. A request without any non-null editable fields
returns **400 Bad Request**. A duplicate email returns **409 Conflict** and the
update is rolled back. The response lists updated field names; when updating
multiple fields, clients should not depend on their ordering in the message.

### Field validation

Registration requires every editable field. PATCH applies the same validation to
the resulting complete profile.

| Field | Current rule |
| --- | --- |
| `email` | Trimmed and lowercased invariantly; at most 50 characters; must match the email regex in `Register.cs`. |
| `first_name`, `last_name` | Trimmed and normalized to initial capitals per space-separated word. Only ASCII letters and spaces between words pass validation; apostrophes, hyphens, and accented letters currently fail. |
| `password` | Trimmed; 8–50 characters; at least one ASCII uppercase letter and one allowed symbol. Only ASCII letters, digits, and `!@#$%^&*_-+=` are allowed. A digit or lowercase letter is not separately required. |
| `school` | Trimmed; must exactly match a stored school name. |

Login also trims the password and trims/lowercases the email. Names and passwords
have deliberate implementation restrictions; consider client validation a
convenience and keep server validation authoritative.

### Errors and authentication lifecycle

| Status | Typical cause |
| --- | --- |
| `400` | Invalid fields, wrong email/password, expired or unknown token, lockout, or an empty update. |
| `401` | Missing/wrong application password, or missing/blank token on a profile request. |
| `409` | Registration or profile update would duplicate an email. |
| `500` | Database/configuration failure or another unexpected server error. Inspect server logs. |

Controller-generated error bodies have a `message` property. ASP.NET Core may
return a validation Problem Details body for malformed JSON or model-binding
errors; callers should handle both shapes. Unsupported body media types may
produce `415` before a controller runs.

Tokens contain 32 random bytes encoded as URL-safe Base64 without padding. New
tokens expire after **21 days**. Login reuses an existing unexpired token and does
not extend its expiration. Profile updates, including password changes, currently
do not revoke tokens.

For a known email, failed logins are counted during an active five-minute attempt
window. At **10 failures**, the account is blocked for a full **five minutes** and
all its tokens are deleted. Every additional group of 10 failures during an active
block extends the timeout by five minutes. Correct credentials do not bypass an
active block, and a successful login does not reset an active failure count.
Unknown emails do not create a tracked user block. After a block expires, log in
again to obtain a valid token.

Policy values are defined in `Env.cs`. Token/block expiration strings are stored
as Mountain local timestamps without an offset. Cleanup runs at startup and daily
at **03:24 Mountain time**, deleting expired token and attempt rows. The scheduler
accounts for daylight-saving transitions, but stored local expiration values have
no explicit UTC offset.

## Development workflow

### Build and test a change

From the repository root:

```sh
dotnet build StudentExchangeBck/StudentExchangeBck.sln
dotnet test StudentExchangeBck/StudentExchangeBck.sln
dotnet test StudentExchangeBck/StudentExchangeBck.sln -c Release
```

To focus on one regression:

```sh
dotnet test StudentExchangeBck/StudentExchangeBck.sln \
  --filter 'FullyQualifiedName~ValidTokenWithLessThanOneDayRemainingIsReused'
```

The current suite has 21 cases covering successful account workflows, validation,
duplicate emails, token reuse/expiry, lockouts, rollback, foreign keys, culture
behavior, concurrent logins, scheduling, and an actual HTTP workflow. Tests run
serially because their fixture temporarily changes the process working directory
and data-directory variable. The HTTP test starts a child API process on an
available loopback port and shuts it down afterward.

When changing a behavior, add a regression that fails for the old behavior. Use
the fixture's disposable database and test-only credentials. Tests should assert
status codes and response fields, as well as persisted state when relevant.

### Database and concurrency model

| Table | Contents |
| --- | --- |
| `Schools` | School IDs and unique display names. |
| `User` | Unique encrypted email, normalized names, encrypted password, school ID, and registration timestamp. |
| `Access_Token` | User ID, unique token, and expiration. Multiple rows per user are permitted by the schema. |
| `Blocked_Users` | User ID, failed-attempt count, and attempt/block expiration. Expired rows can remain until cleanup. |

There is no EF Core model or automatic migration system. Startup expects the
schema to exist; it does not initialize a blank database. `Sql.cs` opens it in
read/write mode with foreign keys enabled. Use the development initializer above
or a compatible database supplied by the team.

`Sql.Read` accepts SELECT statements and returns a dictionary of column-name to
value-list; **zero rows means an empty dictionary**. Check results before indexing.
`Sql.Write` accepts INSERT, UPDATE, and DELETE statements. Bind values through its
parameter dictionary, and select the affected-row expectation deliberately:

- A nonnegative count requires exactly that many affected rows.
- `-1` (default) requires at least one affected row.
- `-2` permits any count, including zero.

A write is committed only after its row-count check passes; exceptions roll back
that individual statement. Separate `Sql.Write` calls do not share a transaction.
The reader/writer lock coordinates database access within one process. Stable
striped user locks coordinate credential checks, token issuance, attempt counts,
and profile updates; different users can share a stripe. These locks do not
coordinate multiple API instances. Do not assume the current design is safe to
scale across processes without revisiting those operations.

For a schema change, plan migration of existing data, adjust the local initializer
and test fixture, and verify both new and existing databases. Back up databases
before manual maintenance and stop the API before moving its data files. School
rows are maintained through database administration, since there is no API for
adding them.

### Implementation boundaries to understand

Emails and passwords currently use deterministic, reversible AES encryption.
Passwords are not stored using a dedicated password hash. The `.env` wrapper also
uses a default key present in source, so encryption of that file alone does not
protect it from someone with both the file and the source. Treat the data files
and configuration as sensitive, restrict file permissions, and use HTTPS outside
local development. A production authentication redesign requires a migration of
existing credentials and an explicit API compatibility plan.

Authentication is performed by `AppAuthJson` inside controllers. The
`UseAuthorization()` middleware does not supply the application's JSON credential
checks. New protected endpoints need the appropriate validation and user-scoped
data access. There is no configured CORS policy, so a browser frontend on another
origin needs a deliberate server configuration change; the GET-body profile
contract also needs consideration.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| Failure before the HTTP listener starts | `Env.GetEnv()` runs before host creation. Check the selected data directory and encrypted `.env`, then the database/schema used by initial cleanup. |
| Missing `.env`, invalid Base64, or decryption failure | Verify that `.env` is encrypted output, not plaintext, and uses the same wrapper salt as `Env.GetEnv()`. |
| Missing database or `no such table` | Ensure `Data.db` exists with all four application tables. Creating an empty SQLite file is insufficient. |
| Existing accounts stop working after configuration changes | Check that `_emailSalt` and `_passSalt` still match the database. Do not regenerate keys for an existing database. |
| Registration returns `400` for a school | Call `/Schools` and copy an exact returned name. |
| Profile request fails despite a login | Supply both credentials in the JSON body; check expiration and account lockout. A placeholder token is not usable. |
| API accepts no requests on the expected port | Check console listener output and launch profile. HTTP uses 5239; HTTPS uses 7000. |
| HTTPS certificate/trust error | Configure the .NET development certificate for your platform, or use the HTTP profile for local development. |
| Browser client cannot read the profile | Browser `fetch` disallows GET bodies; this is an API contract limitation, not a missing Bearer header. |
| `dotnet test` cannot start the HTTP child process | Make sure the .NET host is available. With a custom SDK installation, add it to `PATH` or set `DOTNET_HOST_PATH` to the absolute `dotnet` executable path. |
| Unexpected `500` response | Read server standard error for the underlying exception; error details are deliberately excluded from JSON responses. |

Before committing, review `git status` and `git diff`. Keep credentials, local
SQLite changes, build outputs, and machine-specific IDE settings out of your
change. `bin_/Data.db` is tracked, so modifying it appears in the diff even though
`.env`, `bin/`, and `obj/` are ignored in the application directory.