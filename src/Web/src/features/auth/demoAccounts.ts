export interface DemoAccount {
  email: string
  name: string
  role: string
  organisation: string
}

/**
 * The accounts created by the server-side demo seeder. Listing them keeps the demonstration usable
 * without a credential hand-off; every one of them is a real account subject to the same rules.
 */
export const demoAccounts: DemoAccount[] = [
  {
    email: 'admin@asteria.example',
    name: 'Helena Vasconcelos',
    role: 'TenantAdmin',
    organisation: 'Asteria Group',
  },
  {
    email: 'sofia.martins@asteria.example',
    name: 'Sofia Martins',
    role: 'Manager',
    organisation: 'Asteria Group',
  },
  {
    email: 'ricardo.lopes@asteria.example',
    name: 'Ricardo Lopes',
    role: 'FinanceApprover',
    organisation: 'Asteria Group',
  },
  {
    email: 'ana.ferreira@asteria.example',
    name: 'Ana Ferreira',
    role: 'Employee',
    organisation: 'Asteria Group',
  },
  {
    email: 'contacto@northstar.example',
    name: 'Tiago Ribeiro',
    role: 'ExternalPartner',
    organisation: 'Northstar Supplies',
  },
]

export const demoPassword = 'Demo!Pass123'
