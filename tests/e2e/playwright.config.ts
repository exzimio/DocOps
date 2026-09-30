import { defineConfig, devices } from '@playwright/test'

const webBaseUrl = process.env.E2E_BASE_URL ?? 'http://localhost:5173'
const apiBaseUrl = process.env.E2E_API_URL ?? 'http://localhost:5180'

export default defineConfig({
  testDir: './specs',
  timeout: 45_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : [['list']],
  use: {
    baseURL: webBaseUrl,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    locale: 'pt-PT',
  },
  projects: [
    { name: 'chromium-desktop', use: { ...devices['Desktop Chrome'] } },
    {
      name: 'chromium-mobile',
      use: { ...devices['Pixel 7'] },

      // As capturas são o retrato de secretária, e os dois perfis escrevem nos mesmos ficheiros:
      // corridas em ambos, o último a passar fica com a documentação — e a `dashboard.png` passou
      // mesmo a ter 1082 px de largura. O que precisa de ser verificado ao tamanho do telemóvel são
      // os testes funcionais, que correm nos dois perfis e continuam a correr.
      testIgnore: /(-capture|screenshots)\.spec\.ts$/,
    },
  ],
  webServer: [
    {
      // The API needs the SQL Server container from docker-compose to be running.
      command: 'dotnet run --project ../../src/Api/DocOps.Api.csproj --launch-profile http',
      url: `${apiBaseUrl}/health/ready`,
      reuseExistingServer: true,
      timeout: 180_000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
    {
      command: 'npm run dev',
      cwd: '../../src/Web',
      url: webBaseUrl,
      reuseExistingServer: true,
      timeout: 120_000,
    },
  ],
})
