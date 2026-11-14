<#
  verify-live.ps1 - checks a running Salio API end to end, by hand.

  !!! WARNING: THIS WRITES REAL DATA. RUN IT AGAINST THE DEV DATABASE ONLY, NEVER A REAL SHOP. !!!
  Every run writes, and none of it can be undone (this script never deletes anything):
    - a login (a new trusted-device row for this "phone"),
    - a customer called "Verify Script Customer" (only the first time),
    - a CREDIT SALE of one unit of the first product, with its journal entry and stock movement,
    - a CUSTOMER PAYMENT of half that sale, with its journal entry,
    - a FILED TURNOVER TAX RETURN for January 2020, which PERMANENTLY CLOSES January 2020
      in that shop's books (only the first time; later runs find it already filed).

  Written for Windows PowerShell 5.1 (no &&, no ternary, no ??).

  How to run (from the salio-net folder, with the API running on http://localhost:5077):
    powershell -ExecutionPolicy Bypass -File .\Salio.Tests\verify-live.ps1 -Email you@example.com
  It asks for the password (typing is hidden) and asks you to type YES before it writes.

  Parameters:
    -BaseUrl         default http://localhost:5077
    -Email           the Owner's email (asked if missing)
    -Password        a SecureString (asked with hidden typing if missing). Never printed.
    -Month           the month for turnover tax and the filing pack, as yyyy-MM. Default: this month.
    -OrganizationId  which shop, if the Owner belongs to more than one
    -CustomerPhone   default 0700111222. Found by phone, created if missing.
#>
param(
    [string]$BaseUrl = 'http://localhost:5077',
    [string]$Email,
    [System.Security.SecureString]$Password,
    [string]$Month,
    [string]$OrganizationId,
    [string]$CustomerPhone = '0700111222',
    [string]$CustomerName = 'Verify Script Customer'
)

$ErrorActionPreference = 'Stop'
$Invariant = [System.Globalization.CultureInfo]::InvariantCulture
$Session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$SignedIn = $false

function Kes([long]$minor) {
    return 'KES ' + ([decimal]$minor / 100).ToString('N2', $Invariant)
}

function Step([string]$title, [string]$expect) {
    Write-Host ''
    Write-Host ('=== ' + $title) -ForegroundColor Cyan
    Write-Host ('EXPECT: ' + $expect) -ForegroundColor Yellow
}

function Actual([string]$text) {
    Write-Host ('ACTUAL: ' + $text)
}

function Pass([string]$text) {
    Write-Host ('PASS: ' + $text) -ForegroundColor Green
}

function Fail([string]$text) {
    Write-Host ''
    Write-Host ('FAIL: ' + $text) -ForegroundColor Red
    Write-Host 'Stopped at the first failure. Nothing after this step was run.' -ForegroundColor Red
    if ($script:SignedIn) {
        try { Invoke-Salio 'POST' '/api/auth/logout' $null | Out-Null } catch { }
    }
    exit 1
}

# One HTTP call. Never prints the request body (the login body holds the password).
# Returns Status (number), Data (parsed JSON or $null) and Text (the raw answer).
function Invoke-Salio([string]$method, [string]$path, $body) {
    $uri = $BaseUrl.TrimEnd('/') + $path
    $params = @{
        Uri = $uri
        Method = $method
        WebSession = $script:Session
        UseBasicParsing = $true
    }
    if ($null -ne $body) {
        $json = ConvertTo-Json -InputObject $body -Depth 10
        $params.Body = [System.Text.Encoding]::UTF8.GetBytes($json)
        $params.ContentType = 'application/json; charset=utf-8'
    }

    $status = 0
    $text = ''
    try {
        $response = Invoke-WebRequest @params
        $status = [int]$response.StatusCode
        $text = [string]$response.Content
    }
    catch {
        $errorResponse = $_.Exception.Response
        if ($null -eq $errorResponse) {
            Fail ('Could not reach ' + $uri + ' : ' + $_.Exception.Message + ' -- is the API running?')
        }
        $status = [int]$errorResponse.StatusCode
        $text = $_.ErrorDetails.Message
        if ([string]::IsNullOrEmpty($text)) {
            try {
                $reader = New-Object System.IO.StreamReader($errorResponse.GetResponseStream())
                $text = $reader.ReadToEnd()
            }
            catch { $text = '' }
        }
    }

    $data = $null
    if (-not [string]::IsNullOrWhiteSpace($text)) {
        try { $data = ConvertFrom-Json -InputObject $text } catch { $data = $null }
    }
    return [pscustomobject]@{ Status = $status; Data = $data; Text = $text }
}

# Turns a JSON array answer into a PowerShell list, empty-safe on 5.1.
function AsList($data) {
    # PowerShell unwraps a one-item list on the way out of a function, so a
    # single result arrives as a plain object and .Count on it is empty.
    # Wherever a count matters, the caller wraps the result in @() again.
    return @($data | Where-Object { $null -ne $_ })
}

function Expect-Status($result, [int]$wanted, [string]$what) {
    if ($result.Status -ne $wanted) {
        Fail ($what + ': expected HTTP ' + $wanted + ', got HTTP ' + $result.Status + '. Answer: ' + $result.Text)
    }
}

# ---------------------------------------------------------------------------
Write-Host ''
Write-Host '****************************************************************' -ForegroundColor Red
Write-Host ' This script WRITES REAL DATA to the Salio API at:' -ForegroundColor Red
Write-Host ('   ' + $BaseUrl) -ForegroundColor Red
Write-Host ' A credit sale, a customer payment, and a filed tax return that' -ForegroundColor Red
Write-Host ' PERMANENTLY CLOSES January 2020. Nothing is ever deleted.' -ForegroundColor Red
Write-Host ' Run it against the DEV database only, never a real shop.' -ForegroundColor Red
Write-Host '****************************************************************' -ForegroundColor Red
if ($BaseUrl -notmatch '^https?://(localhost|127\.0\.0\.1)(:|/|$)') {
    Write-Host ('That address is not localhost. Be very sure it is not a real shop.') -ForegroundColor Red
}
$answer = Read-Host 'Type YES to carry on'
if ($answer -cne 'YES') {
    Write-Host 'Stopped. Nothing was written.'
    exit 1
}

if ([string]::IsNullOrWhiteSpace($Email)) {
    $Email = Read-Host 'Owner email'
}
if ($null -eq $Password) {
    $Password = Read-Host 'Password (hidden)' -AsSecureString
}

$today = Get-Date
$todayText = $today.ToString('yyyy-MM-dd', $Invariant)
if ([string]::IsNullOrWhiteSpace($Month)) {
    $Month = $today.ToString('yyyy-MM', $Invariant)
}
try {
    $monthStart = [datetime]::ParseExact($Month + '-01', 'yyyy-MM-dd', $Invariant)
}
catch {
    Write-Host ('-Month must look like 2026-11, not "' + $Month + '"') -ForegroundColor Red
    exit 1
}
$monthEnd = $monthStart.AddMonths(1).AddDays(-1)
$fromText = $monthStart.ToString('yyyy-MM-dd', $Invariant)
$toText = $monthEnd.ToString('yyyy-MM-dd', $Invariant)
$todayInMonth = ($today.Date -ge $monthStart) -and ($today.Date -le $monthEnd)

# ---------------------------------------------------------------------------
Step '1. Sign in' 'HTTP 200; the account is an Owner (role 0) of the shop; mustChangePassword is false.'

$bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
try {
    $loginBody = @{ email = $Email; password = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
}
finally {
    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
}
$login = Invoke-Salio 'POST' '/api/auth/login' $loginBody
$loginBody = $null
Expect-Status $login 200 'Sign in'
$SignedIn = $true

if ($login.Data.mustChangePassword -eq $true) {
    Fail 'This account must choose a new password first. Sign in on the till, choose one, then run this again.'
}

# @() so one shop counts as 1, not as nothing.
$shops = @(AsList $login.Data.shops)
$shopCount = $shops.Count
$shop = $null
if (-not [string]::IsNullOrWhiteSpace($OrganizationId)) {
    foreach ($s in $shops) {
        if ([string]$s.organizationId -eq $OrganizationId) { $shop = $s }
    }
    if ($null -eq $shop) { Fail ('This account does not belong to shop ' + $OrganizationId) }
}
elseif ($shopCount -eq 1) {
    $shop = $shops[0]
}
elseif ($shopCount -eq 0) {
    Fail 'This account belongs to no shop, so there is nothing to check.'
}
else {
    # Only here, with two or more shops, is -OrganizationId needed.
    $names = ($shops | ForEach-Object { $_.name + ' = ' + $_.organizationId }) -join '; '
    Fail ('This account belongs to ' + $shopCount + ' shops. Run again with -OrganizationId. Shops: ' + $names)
}
$orgId = [string]$shop.organizationId
Actual ('signed in as ' + $login.Data.fullName + ', shop "' + $shop.name + '", role ' + $shop.role)
if ([string]$shop.role -ne '0' -and [string]$shop.role -ne 'Owner') {
    Fail 'This account is not an Owner of that shop. Payments, reports and tax are Owner only.'
}
Pass 'signed in as an Owner'

# ---------------------------------------------------------------------------
Step '2. Pick a product' 'HTTP 200 and at least one active product with a selling price of at least KES 0.02.'

$products = Invoke-Salio 'GET' ('/api/products?organizationId=' + $orgId) $null
Expect-Status $products 200 'Product list'
$product = $null
foreach ($p in (AsList $products.Data)) {
    if ($null -eq $product -and $p.isActive -eq $true -and [long]$p.sellPriceMinor -ge 2) { $product = $p }
}
if ($null -eq $product) { Fail 'No active product with a price. Add one on the Parts screen first.' }
$priceMinor = [long]$product.sellPriceMinor
Actual ($product.name + ' (' + $product.sku + ') at ' + (Kes $priceMinor) + ', on hand ' + $product.quantityOnHand)
Pass 'product chosen'

# ---------------------------------------------------------------------------
Step '3. Find or create the customer' ('HTTP 200; a customer with phone ' + $CustomerPhone + ' exists afterwards.')

$customers = Invoke-Salio 'GET' ('/api/customers?organizationId=' + $orgId) $null
Expect-Status $customers 200 'Customer list'
$customer = $null
foreach ($c in (AsList $customers.Data)) {
    if ($c.phone -eq $CustomerPhone) { $customer = $c }
}
if ($null -eq $customer) {
    $created = Invoke-Salio 'POST' '/api/customers' @{ organizationId = $orgId; name = $CustomerName; phone = $CustomerPhone }
    Expect-Status $created 200 'Create customer'
    $customer = $created.Data
    Actual ('created ' + $customer.name + ' (' + $customer.id + ')')
}
else {
    Actual ('found ' + $customer.name + ' (' + $customer.id + ')')
}
$customerId = [string]$customer.id
$balanceBefore = [long]$customer.balanceMinor
Actual ('owes ' + (Kes $balanceBefore) + ' before this run')
Pass 'customer ready'

# ---------------------------------------------------------------------------
$expectedAfterSale = $balanceBefore + $priceMinor
Step '4. Sell 1 unit on credit' ('HTTP 200; totalMinor = ' + $priceMinor + ' (' + (Kes $priceMinor) + '); the customer now owes ' + (Kes $expectedAfterSale) + '.')

$saleBody = @{
    organizationId = $orgId
    saleDate = $todayText
    customerId = $customerId
    lines = @(@{ productId = [string]$product.id; quantity = 1; unitPriceMinor = $priceMinor; taxBand = 'A' })
    payments = @(@{ method = 3; amountMinor = $priceMinor })    # 3 = Credit (PaymentMethod enum: Cash 0, Mpesa 1, Card 2, Credit 3)
}
$sale = Invoke-Salio 'POST' '/api/sales' $saleBody
Expect-Status $sale 200 'Credit sale'
Actual ('sale ' + $sale.Data.saleNumber + ', total ' + (Kes ([long]$sale.Data.totalMinor)) + ', customer owes ' + (Kes ([long]$sale.Data.customerBalanceMinor)))
if ([long]$sale.Data.totalMinor -ne $priceMinor) { Fail 'The sale total is not the price of one unit.' }
if ([long]$sale.Data.customerBalanceMinor -ne $expectedAfterSale) { Fail 'The customer balance did not go up by the sale total.' }
Pass 'credit sale recorded'

# ---------------------------------------------------------------------------
$partMinor = [long][math]::Floor($priceMinor / 2)
$expectedAfterPayment = $expectedAfterSale - $partMinor
Step '5. Take a part payment in cash' ('HTTP 200; paid ' + (Kes $partMinor) + '; balanceAfterMinor = ' + $expectedAfterPayment + ' (' + (Kes $expectedAfterPayment) + ').')

$payment = Invoke-Salio 'POST' '/api/customer-payments' @{
    organizationId = $orgId
    customerId = $customerId
    amountMinor = $partMinor
    paymentDate = $todayText
    paidWith = 'Cash'
}
Expect-Status $payment 200 'Customer payment'
Actual ('paid ' + (Kes ([long]$payment.Data.amountMinor)) + ', still owes ' + (Kes ([long]$payment.Data.balanceAfterMinor)))
if ([long]$payment.Data.balanceAfterMinor -ne $expectedAfterPayment) { Fail 'The balance after the payment is wrong.' }
Pass 'part payment recorded'

# ---------------------------------------------------------------------------
Step "6. Read the customer's balance back" ('the customer list shows ' + (Kes $expectedAfterPayment) + ' owed.')

$customers = Invoke-Salio 'GET' ('/api/customers?organizationId=' + $orgId) $null
Expect-Status $customers 200 'Customer list'
$readBack = $null
foreach ($c in (AsList $customers.Data)) {
    if ([string]$c.id -eq $customerId) { $readBack = $c }
}
if ($null -eq $readBack) { Fail 'The customer has disappeared from the list.' }
Actual ($readBack.name + ' owes ' + (Kes ([long]$readBack.balanceMinor)))
if ([long]$readBack.balanceMinor -ne $expectedAfterPayment) { Fail 'The balance in the customer list does not match.' }
Pass 'balance matches'

# ---------------------------------------------------------------------------
Step ('7. Trial balance as of ' + $todayText) 'HTTP 200; total debits EQUAL total credits.'

$tb = Invoke-Salio 'GET' ('/api/reports/trial-balance?organizationId=' + $orgId + '&asOf=' + $todayText) $null
Expect-Status $tb 200 'Trial balance'
$debits = [long]$tb.Data.totalDebitMinor
$credits = [long]$tb.Data.totalCreditMinor
Actual ('debits ' + (Kes $debits) + ', credits ' + (Kes $credits))
if ($debits -ne $credits) { Fail 'The books do not balance: debits and credits differ.' }
Pass 'debits equal credits'

# ---------------------------------------------------------------------------
$expectTurnover = 'HTTP 200 with gross turnover, rate and tax due for ' + $fromText + ' to ' + $toText + '.'
if ($todayInMonth) { $expectTurnover = $expectTurnover + ' Gross turnover is at least ' + (Kes $priceMinor) + ' (it includes the sale just made).' }
Step ('8. Turnover tax for ' + $Month) $expectTurnover

$turnover = Invoke-Salio 'GET' ('/api/tax/turnover-tax?organizationId=' + $orgId + '&from=' + $fromText + '&to=' + $toText) $null
Expect-Status $turnover 200 'Turnover tax'
$gross = [long]$turnover.Data.grossTurnoverMinor
$taxDue = [long]$turnover.Data.taxDueMinor
Actual ('gross ' + (Kes $gross) + ', rate ' + $turnover.Data.ratePercent + '% (from ' + $turnover.Data.rateEffectiveFrom + ', source: ' + $turnover.Data.rateSource + '), tax due ' + (Kes $taxDue))
if ($todayInMonth -and $gross -lt $priceMinor) { Fail 'Gross turnover is less than the sale just made.' }
Pass 'turnover tax worked out'

# ---------------------------------------------------------------------------
Step ('9. Filing pack for ' + $Month) ('HTTP 200; the same gross turnover (' + (Kes $gross) + ') and tax due (' + (Kes $taxDue) + ') as step 8. Exceptions are listed for you to read.')

$pack = Invoke-Salio 'GET' ('/api/tax/filing-pack?organizationId=' + $orgId + '&from=' + $fromText + '&to=' + $toText) $null
Expect-Status $pack 200 'Filing pack'
Actual ('sales ' + $pack.Data.saleCount + ', gross ' + (Kes ([long]$pack.Data.grossTurnoverMinor)) + ', tax due ' + (Kes ([long]$pack.Data.taxDueMinor)))
$exceptions = @(AsList $pack.Data.exceptions)
if ($exceptions.Count -eq 0) {
    Actual 'no exceptions'
}
else {
    foreach ($e in $exceptions) { Actual ('exception: ' + $e) }
}
if ([long]$pack.Data.grossTurnoverMinor -ne $gross -or [long]$pack.Data.taxDueMinor -ne $taxDue) {
    Fail 'The filing pack disagrees with the turnover tax figure.'
}
Pass 'filing pack matches'

# ---------------------------------------------------------------------------
Step '10. File a Turnover Tax return for January 2020 (closes that month for good)' ('HTTP 200 the first time. On a later run: HTTP 400 "A TurnoverTax return has already been recorded for part of 2020-01-01 to 2020-01-31", which is also fine.')

$filed = Invoke-Salio 'POST' '/api/tax/filed-returns' @{
    organizationId = $orgId
    periodFrom = '2020-01-01'
    periodTo = '2020-01-31'
    taxType = 0                                   # 0 = TurnoverTax (TaxType enum: TurnoverTax 0, Vat 1)
    amountMinor = 0
    filedAt = '2020-02-15T09:00:00+03:00'
    acknowledgementReference = 'VERIFY-SCRIPT-2020-01'
}
if ($filed.Status -eq 200) {
    Actual ('filed, id ' + $filed.Data.id)
}
elseif ($filed.Status -eq 400 -and $filed.Text -match 'already been recorded') {
    Actual 'already filed by an earlier run'
}
else {
    Fail ('Filing the return: HTTP ' + $filed.Status + '. Answer: ' + $filed.Text)
}
Pass 'January 2020 is filed'

# ---------------------------------------------------------------------------
Step '11. Try a journal entry dated 2020-01-15 (inside the filed month)' ('REFUSED with HTTP 400, title "The journal entry was rejected", detail "The books for 2020-01-15 are closed because a tax return has been filed for that period. Post the correction in a period that is still open."')

$accounts = Invoke-Salio 'GET' ('/api/accounts?organizationId=' + $orgId) $null
Expect-Status $accounts 200 'Chart of accounts'
$cash = $null
foreach ($a in (AsList $accounts.Data)) {
    if ($a.code -eq '1000') { $cash = $a }
}
if ($null -eq $cash) { Fail 'Account 1000 Cash is missing from the chart of accounts.' }

# Debit and credit the SAME account by KES 1.00. Balanced, so the only reason to
# refuse it is the closed period. If the lock were broken and it went through,
# it would change no balance at all.
$journal = Invoke-Salio 'POST' '/api/journal-entries' @{
    organizationId = $orgId
    entryDate = '2020-01-15'
    description = 'verify-live.ps1: must be refused, January 2020 is filed'
    sourceType = 0                                # 0 = Manual
    lines = @(
        @{ accountId = [string]$cash.id; debitMinor = 100; creditMinor = 0; currency = 'KES' },
        @{ accountId = [string]$cash.id; debitMinor = 0; creditMinor = 100; currency = 'KES' }
    )
}
Actual ('HTTP ' + $journal.Status + ': ' + $journal.Text)
if ($journal.Status -ne 400) {
    Fail 'The closed period ACCEPTED a journal entry. The period lock is broken (a net-zero KES 1.00 entry on 1000 Cash was written on 2020-01-15).'
}
if ($journal.Text -notmatch 'are closed because a tax return has been filed') {
    Fail 'Refused, but not because the period is closed. Read the detail above.'
}
Pass 'the filed month refused the entry'

# ---------------------------------------------------------------------------
Invoke-Salio 'POST' '/api/auth/logout' $null | Out-Null
$SignedIn = $false
Write-Host ''
Write-Host 'ALL CHECKS PASSED.' -ForegroundColor Green
exit 0
