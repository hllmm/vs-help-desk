import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import type { AssignTicketResult, AssignableUser } from '../../api/types'
import { TicketAssignmentPanel } from './TicketAssignmentPanel'

const fetchAssignableUsers = vi.hoisted(() => vi.fn())
const assignTicket = vi.hoisted(() => vi.fn())

vi.mock('../../api/ticketsApi', () => ({
  fetchAssignableUsers,
  assignTicket,
}))

const SUPPORT_ID = '11111111-1111-1111-1111-111111111111'
const ADMIN_ID = '22222222-2222-2222-2222-222222222222'

const users: AssignableUser[] = [
  { id: SUPPORT_ID, fullName: 'Ada Destek', username: 'ada.destek' },
  { id: ADMIN_ID, fullName: 'Ece Yönetici', username: 'ece.admin' },
]

function assignment(
  assignedUserId: string | null,
  changed = true,
): AssignTicketResult {
  return {
    ticketId: 'ticket-1',
    assignedUserId,
    updatedAt: '2026-07-21T12:00:00.000Z',
    changed,
  }
}

describe('TicketAssignmentPanel', () => {
  beforeEach(() => {
    fetchAssignableUsers.mockReset()
    assignTicket.mockReset()
    fetchAssignableUsers.mockResolvedValue(users)
  })

  it('loads active users and saves a changed assignee', async () => {
    assignTicket.mockResolvedValueOnce(assignment(ADMIN_ID))
    const onApplyAssignment = vi.fn()
    const user = userEvent.setup()

    render(
      <TicketAssignmentPanel
        ticketId="ticket-1"
        status="CustomerReplied"
        assignedUserId={SUPPORT_ID}
        onApplyAssignment={onApplyAssignment}
      />,
    )

    const select = await screen.findByLabelText('Atanan destek personeli')
    expect(select).toHaveValue(SUPPORT_ID)
    expect(screen.getByRole('option', { name: 'Ada Destek (@ada.destek)' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Atamayı kaydet' })).toBeDisabled()

    await user.selectOptions(select, ADMIN_ID)
    await user.click(screen.getByRole('button', { name: 'Atamayı kaydet' }))

    expect(assignTicket).toHaveBeenCalledWith('ticket-1', ADMIN_ID, {
      signal: expect.any(AbortSignal),
    })
    expect(onApplyAssignment).toHaveBeenCalledWith(assignment(ADMIN_ID))
    expect(await screen.findByRole('status')).toHaveTextContent('Sorumlu güncellendi.')
    expect(screen.getByRole('button', { name: 'Atamayı kaydet' })).toBeDisabled()
  })

  it('sends null when Atanmamış is selected', async () => {
    assignTicket.mockResolvedValueOnce(assignment(null))
    const onApplyAssignment = vi.fn()
    const user = userEvent.setup()

    render(
      <TicketAssignmentPanel
        ticketId="ticket-1"
        status="New"
        assignedUserId={SUPPORT_ID}
        onApplyAssignment={onApplyAssignment}
      />,
    )

    const select = await screen.findByLabelText('Atanan destek personeli')
    await user.selectOptions(select, '')
    await user.click(screen.getByRole('button', { name: 'Atamayı kaydet' }))

    expect(assignTicket).toHaveBeenCalledWith('ticket-1', null, {
      signal: expect.any(AbortSignal),
    })
    expect(onApplyAssignment).toHaveBeenCalledWith(assignment(null))
  })

  it('shows a stale current assignee and lets the operator replace it', async () => {
    const staleId = '99999999-9999-9999-9999-999999999999'
    render(
      <TicketAssignmentPanel
        ticketId="ticket-1"
        status="New"
        assignedUserId={staleId}
        onApplyAssignment={vi.fn()}
      />,
    )

    const select = await screen.findByLabelText('Atanan destek personeli')
    expect(select).toHaveValue(staleId)
    expect(
      screen.getByRole('option', { name: 'Mevcut atama (aktif değil)' }),
    ).toBeDisabled()
  })

  it('offers retry after assignee list network failure', async () => {
    fetchAssignableUsers
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValueOnce(users)
    const user = userEvent.setup()

    render(
      <TicketAssignmentPanel
        ticketId="ticket-1"
        status="New"
        assignedUserId={null}
        onApplyAssignment={vi.fn()}
      />,
    )

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Sorumlu listesi yüklenemedi. Bağlantınızı kontrol edip yeniden deneyin.',
    )
    await user.click(screen.getByRole('button', { name: 'Listeyi yeniden yükle' }))

    expect(await screen.findByLabelText('Atanan destek personeli')).toBeEnabled()
    expect(fetchAssignableUsers).toHaveBeenCalledTimes(2)
  })

  it('maps stale-user mutation without exposing backend text', async () => {
    assignTicket.mockRejectedValueOnce(
      new ApiError(400, 'raw backend', {
        code: 'assignee-not-available',
        detail: 'secret upstream text',
      }),
    )
    const user = userEvent.setup()

    render(
      <TicketAssignmentPanel
        ticketId="ticket-1"
        status="New"
        assignedUserId={null}
        onApplyAssignment={vi.fn()}
      />,
    )

    const select = await screen.findByLabelText('Atanan destek personeli')
    await user.selectOptions(select, ADMIN_ID)
    await user.click(screen.getByRole('button', { name: 'Atamayı kaydet' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(
      'Seçilen kullanıcı artık aktif değil. Listeyi yeniden yükleyin.',
    )
    expect(alert).not.toHaveTextContent('secret upstream text')
    expect(alert).not.toHaveTextContent('raw backend')
  })

  it('keeps resolved ticket ownership visible but disables changes', async () => {
    render(
      <TicketAssignmentPanel
        ticketId="ticket-1"
        status="Resolved"
        assignedUserId={SUPPORT_ID}
        onApplyAssignment={vi.fn()}
      />,
    )

    expect(await screen.findByLabelText('Atanan destek personeli')).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Atamayı kaydet' })).toBeDisabled()
    expect(screen.getByText('Çözülmüş taleplerde sorumlu değiştirilemez.')).toBeInTheDocument()
    await waitFor(() => expect(fetchAssignableUsers).toHaveBeenCalledTimes(1))
    expect(assignTicket).not.toHaveBeenCalled()
  })
})
