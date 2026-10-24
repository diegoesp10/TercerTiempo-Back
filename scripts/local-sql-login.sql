-- Login SQL para que la API en Docker (Linux, sin autenticación de Windows) use el SQL Server local del equipo
-- (Start-Docker.ps1 -Database Local). Ejecutar en SSMS conectado al servidor local, cambiando la contraseña.
-- Antes, en el servidor:
--   1. Propiedades del servidor > Seguridad > «Modo de autenticación de SQL Server y Windows», y reiniciar el servicio.
--   2. SQL Server Configuration Manager > Protocolos de MSSQLSERVER > TCP/IP habilitado (puerto 1433), y reiniciar.
-- La misma contraseña va en .env.development: TT_LOCAL_SQL_PASSWORD.
USE [master];
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'tercertiempo_app')
    CREATE LOGIN [tercertiempo_app] WITH PASSWORD = N'CAMBIA-ESTA-CONTRASEÑA', CHECK_POLICY = ON, DEFAULT_DATABASE = [TercerTiempo];
GO
USE [TercerTiempo];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'tercertiempo_app')
    CREATE USER [tercertiempo_app] FOR LOGIN [tercertiempo_app];
-- db_owner: la API aplica las migraciones al arrancar (crea y cambia tablas).
ALTER ROLE [db_owner] ADD MEMBER [tercertiempo_app];
GO
