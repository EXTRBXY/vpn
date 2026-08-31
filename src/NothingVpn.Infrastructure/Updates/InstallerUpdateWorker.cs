namespace NothingVpn.Infrastructure.Updates;

internal static class InstallerUpdateWorker
{
    // Runs in system Windows PowerShell under a verified, non-elevated token. No script files or profile loading.
    internal const string Script = """
        $ErrorActionPreference = 'Stop'
        $committed = $false
        $pipe = $null
        try {
            $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
            $principal = [Security.Principal.WindowsPrincipal]::new($identity)
            if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Процесс обновления не должен иметь права администратора.' }
            $security = [IO.Pipes.PipeSecurity]::new()
            $security.SetAccessRuleProtection($true, $false)
            $security.AddAccessRule([IO.Pipes.PipeAccessRule]::new($identity.User, [IO.Pipes.PipeAccessRights]::FullControl, [Security.AccessControl.AccessControlType]::Allow))
            $pipe = [IO.Pipes.NamedPipeServerStream]::new($env:NOTHINGVPN_UPDATE_PIPE, [IO.Pipes.PipeDirection]::InOut, 1, [IO.Pipes.PipeTransmissionMode]::Byte, [IO.Pipes.PipeOptions]::Asynchronous, 1024, 1024, $security)
            $connection = $pipe.BeginWaitForConnection($null, $null)
            if (!$connection.AsyncWaitHandle.WaitOne(15000)) { throw 'Истекло время подготовки обновления.' }
            $pipe.EndWaitForConnection($connection)
            $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8)
            $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false))
            $writer.AutoFlush = $true
            $parent = [Diagnostics.Process]::GetProcessById([int]$env:NOTHINGVPN_UPDATE_PARENT)
            if ($parent.StartTime.ToUniversalTime().Ticks -ne [long]$env:NOTHINGVPN_UPDATE_STARTED) { throw 'Исходный процесс приложения изменился.' }
            if ($parent.WaitForExit(0)) { throw 'Исходный процесс приложения уже завершён.' }
            if (![IO.File]::Exists($env:NOTHINGVPN_UPDATE_EXE)) { throw 'Файл установщика недоступен.' }
            $writer.WriteLine('READY')
            $authorization = $reader.ReadLineAsync()
            if (!$authorization.Wait(15000) -or $authorization.Result -ne 'COMMIT') { throw 'Обновление отменено.' }
            $writer.WriteLine('COMMITTED')
            $committed = $true
            $pipe.Dispose()
            if (!$parent.WaitForExit(120000)) { throw 'Приложение не завершилось. Обновление не запущено.' }
            $start = [Diagnostics.ProcessStartInfo]::new()
            $start.FileName = $env:NOTHINGVPN_UPDATE_EXE
            $start.Arguments = $env:NOTHINGVPN_UPDATE_ARGS
            $start.UseShellExecute = $false
            $start.WorkingDirectory = [IO.Path]::GetDirectoryName($start.FileName)
            $child = [Diagnostics.Process]::Start($start)
            if ($null -eq $child) { throw 'Не удалось запустить установщик.' }
            $child.Dispose()
            exit 0
        } catch {
            if ($committed) {
                Add-Type -AssemblyName System.Windows.Forms
                [void][Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Nothing VPN — обновление')
            } elseif ($null -ne $pipe -and $pipe.IsConnected -and $null -ne $writer) {
                try { $writer.WriteLine('ERROR: ' + $_.Exception.Message) } catch { }
            }
            exit 1
        } finally {
            if ($null -ne $pipe) { $pipe.Dispose() }
        }
        """;
}
