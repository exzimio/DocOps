import type { LucideIcon } from 'lucide-react'
import { CheckSquare, FileText, GitBranch, LayoutDashboard } from 'lucide-react'
import { Permission } from '@/lib/types'

export interface NavItem {
  /** Translation key under `nav`. */
  labelKey: string
  to: string
  icon: LucideIcon
  /** Permission required to see the entry. The server enforces the same rule on every endpoint. */
  permission?: string
}

export interface NavSection {
  labelKey: string
  items: NavItem[]
}

/**
 * Navigation is data-driven and grows one phase at a time: an entry only appears here once its
 * route, API and persistence exist, so the shell never contains a link that leads nowhere.
 */
export const navigationSections: NavSection[] = [
  {
    labelKey: 'nav.sectionWork',
    items: [
      { labelKey: 'nav.dashboard', to: '/app/dashboard', icon: LayoutDashboard },
      {
        labelKey: 'nav.documents',
        to: '/app/documents',
        icon: FileText,
        permission: Permission.documentsView,
      },
      {
        labelKey: 'nav.tasks',
        to: '/app/tasks',
        icon: CheckSquare,
        permission: Permission.tasksViewOwn,
      },
      {
        labelKey: 'nav.workflows',
        to: '/app/workflows',
        icon: GitBranch,
        permission: Permission.workflowsView,
      },
    ],
  },
]
