import { useState } from 'react'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import type { ResolveTicketResult } from '../../api/types'
import { TicketResolutionPanel } from './TicketResolutionPanel'
import { RESOLUTION_COPY } from './useResolveTicket'

const resolveTicket = vi.hoisted(() => vi.fn())

vi.mock('../../api/ticketsApi', () => ({
  resolveTicket,
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function sampleResult(
  overrides: Partial<ResolveTicketResult> = {},
): ResolveTicketResult {
  return {
    ticketId: 'ticket-1',
    ticketNumber: 'VS-000001',
    status: 'Resolved',
    resolvedAt: '2026-07-20T12:00:00.000Z',
    updatedAt: '2026-07-20T12:00:00.000Z',
    lastActivityAt: '2026-07-20T12:00:00.000Z',
    closedByUserId: 'user-1',
    changed: true,
    ...overrides,
  }
}

function renderPanel(
  props: Partial<{
    ticketId: string
    status: string
    onApplyResolved: (result: ResolveTicketResult) => void
    onRefresh: () => Promise<void>
  }> = {},
) {
  const onApplyResolved = props.onApplyResolved ?? vi.fn()
  const onRefresh = props.onRefresh ?? vi.fn().mockResolvedValue(undefined)
  const user = userEvent.setup()
  const view = render(
    <div>
      <form
        onSubmit={(event) => {
          event.preventDefault()
          throw new Error('reply form must not submit from resolution dialog')
        }}
      >
        <button type="submit">Yanıtı gönder</button>
      </form>
      <TicketResolutionPanel
        ticketId={props.ticketId ?? 'ticket-1'}
        status={props.status ?? 'New'}
        onApplyResolved={onApplyResolved}
        onRefresh={onRefresh}
      />
    </div>,
  )
  return { user, onApplyResolved, onRefresh, ...view }
}

describe('TicketResolutionPanel', () => {
  beforeEach(() => {
    resolveTicket.mockReset()
  })

  it('shows resolve trigger for open status', () => {
    renderPanel({ status: 'WaitingCustomerReply' })

    expect(
      screen.getByRole('button', { name: RESOLUTION_COPY.trigger }),
    ).toBeInTheDocument()
    expect(screen.queryByText(RESOLUTION_COPY.closureNote)).not.toBeInTheDocument()
  })

  it('shows closure note and no resolve action for initial Resolved status', () => {
    renderPanel({ status: 'Resolved' })

    expect(screen.getByText(RESOLUTION_COPY.closureNote)).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: RESOLUTION_COPY.trigger }),
    ).not.toBeInTheDocument()
  })

  it('opens an alert dialog with safe initial focus on Vazgeç', async () => {
    const { user } = renderPanel()

    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.trigger }),
    )

    const dialog = screen.getByRole('alertdialog')
    expect(dialog).toHaveAccessibleName(RESOLUTION_COPY.dialogTitle)
    expect(dialog).toHaveAccessibleDescription(RESOLUTION_COPY.dialogDescription)
    expect(
      screen.getByRole('button', { name: RESOLUTION_COPY.cancel }),
    ).toHaveFocus()
  })

  it('closes on Escape/cancel and returns focus to the trigger', async () => {
    const { user } = renderPanel()
    const trigger = screen.getByRole('button', {
      name: RESOLUTION_COPY.trigger,
    })

    await user.click(trigger)
    await user.keyboard('{Escape}')

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    expect(trigger).toHaveFocus()

    await user.click(trigger)
    await user.click(screen.getByRole('button', { name: RESOLUTION_COPY.cancel }))
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    expect(trigger).toHaveFocus()
  })

  it('confirm busy disables dialog controls, shows busy label, and posts once', async () => {
    const pending = deferred<ResolveTicketResult>()
    resolveTicket.mockReturnValueOnce(pending.promise)
    const { user, onApplyResolved, onRefresh } = renderPanel()

    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.trigger }),
    )
    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.confirm }),
    )

    expect(
      screen.getByRole('button', { name: RESOLUTION_COPY.busy }),
    ).toBeDisabled()
    expect(
      screen.getByRole('button', { name: RESOLUTION_COPY.cancel }),
    ).toBeDisabled()
    expect(document.querySelector('.ticket-resolution')).toHaveAttribute(
      'aria-busy',
      'true',
    )

    await user.keyboard('{Escape}')
    expect(screen.getByRole('alertdialog')).toBeInTheDocument()

    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.busy }),
    )
    expect(resolveTicket).toHaveBeenCalledTimes(1)

    pending.resolve(sampleResult())
    await waitFor(() => {
      expect(onApplyResolved).toHaveBeenCalledTimes(1)
    })
    expect(onRefresh).toHaveBeenCalledTimes(1)
  })

  it('success applies result, refreshes, closes dialog, and focuses resolved notice', async () => {
    resolveTicket.mockResolvedValueOnce(sampleResult({ changed: true }))
    const onApplyResolved = vi.fn()
    const onRefresh = vi.fn().mockResolvedValue(undefined)
    const user = userEvent.setup()

    function Harness() {
      const [status, setStatus] = useState('New')
      return (
        <TicketResolutionPanel
          ticketId="ticket-1"
          status={status}
          onApplyResolved={(result) => {
            onApplyResolved(result)
            setStatus(result.status)
          }}
          onRefresh={onRefresh}
        />
      )
    }

    render(<Harness />)

    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.trigger }),
    )
    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.confirm }),
    )

    await waitFor(() => {
      expect(onApplyResolved).toHaveBeenCalledWith(sampleResult({ changed: true }))
    })
    expect(onRefresh).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()

    const notice = await screen.findByText(RESOLUTION_COPY.resolved)
    expect(notice).toHaveFocus()
    expect(notice).toHaveAttribute('role', 'status')
    expect(screen.getByText(RESOLUTION_COPY.closureNote)).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: RESOLUTION_COPY.trigger }),
    ).not.toBeInTheDocument()
  })

  it('already resolved applies result, refreshes, and shows exact no-op notice', async () => {
    resolveTicket.mockResolvedValueOnce(sampleResult({ changed: false }))
    const onApplyResolved = vi.fn()
    const onRefresh = vi.fn().mockResolvedValue(undefined)
    const { user } = renderPanel({ onApplyResolved, onRefresh })

    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.trigger }),
    )
    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.confirm }),
    )

    expect(await screen.findByText(RESOLUTION_COPY.alreadyResolved)).toHaveFocus()
    expect(onApplyResolved).toHaveBeenCalledWith(
      sampleResult({ changed: false }),
    )
    expect(onRefresh).toHaveBeenCalledTimes(1)
  })

  it('409 does not apply, refreshes once, and shows exact conflict notice', async () => {
    resolveTicket.mockRejectedValueOnce(new ApiError(409, 'raw-conflict'))
    const onApplyResolved = vi.fn()
    const onRefresh = vi.fn().mockResolvedValue(undefined)
    const { user } = renderPanel({ onApplyResolved, onRefresh })

    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.trigger }),
    )
    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.confirm }),
    )

    expect(await screen.findByText(RESOLUTION_COPY.conflict)).toHaveFocus()
    expect(onApplyResolved).not.toHaveBeenCalled()
    expect(onRefresh).toHaveBeenCalledTimes(1)
    expect(screen.queryByText('raw-conflict')).not.toBeInTheDocument()
    expect(resolveTicket).toHaveBeenCalledTimes(1)
  })

  it('network ambiguity does not apply, refreshes once, and never retries resolve', async () => {
    resolveTicket.mockRejectedValueOnce(new TypeError('Failed to fetch'))
    const onApplyResolved = vi.fn()
    const onRefresh = vi.fn().mockResolvedValue(undefined)
    const { user } = renderPanel({ onApplyResolved, onRefresh })

    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.trigger }),
    )
    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.confirm }),
    )

    expect(await screen.findByText(RESOLUTION_COPY.network)).toHaveFocus()
    expect(onApplyResolved).not.toHaveBeenCalled()
    expect(onRefresh).toHaveBeenCalledTimes(1)
    expect(resolveTicket).toHaveBeenCalledTimes(1)
  })

  it('404 and server errors show fixed Turkish copy without raw backend text', async () => {
    resolveTicket.mockRejectedValueOnce(
      new ApiError(404, 'backend-missing-text'),
    )
    const { user, onApplyResolved, onRefresh } = renderPanel()

    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.trigger }),
    )
    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.confirm }),
    )

    expect(await screen.findByText(RESOLUTION_COPY.notFound)).toHaveFocus()
    expect(onApplyResolved).not.toHaveBeenCalled()
    expect(onRefresh).not.toHaveBeenCalled()
    expect(screen.queryByText('backend-missing-text')).not.toBeInTheDocument()

    resolveTicket.mockRejectedValueOnce(
      new ApiError(500, 'upstream-raw-body'),
    )
    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.trigger }),
    )
    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.confirm }),
    )

    expect(await screen.findByText(RESOLUTION_COPY.serverError)).toHaveFocus()
    expect(screen.queryByText('upstream-raw-body')).not.toBeInTheDocument()
  })

  it('protected 401 produces no transient local alert', async () => {
    resolveTicket.mockRejectedValueOnce(new ApiError(401, 'Unauthorized'))
    const { user, onApplyResolved, onRefresh } = renderPanel()

    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.trigger }),
    )
    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.confirm }),
    )

    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    })
    expect(onApplyResolved).not.toHaveBeenCalled()
    expect(onRefresh).not.toHaveBeenCalled()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('traps focus inside the open dialog and never submits the reply form', async () => {
    const { user } = renderPanel()

    await user.click(
      screen.getByRole('button', { name: RESOLUTION_COPY.trigger }),
    )

    const dialog = screen.getByRole('alertdialog')
    const cancel = within(dialog).getByRole('button', {
      name: RESOLUTION_COPY.cancel,
    })
    const confirm = within(dialog).getByRole('button', {
      name: RESOLUTION_COPY.confirm,
    })

    expect(cancel).toHaveFocus()
    await user.tab()
    expect(confirm).toHaveFocus()
    await user.tab()
    expect(cancel).toHaveFocus()
    await user.tab({ shift: true })
    expect(confirm).toHaveFocus()

    await user.click(cancel)
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })
})
