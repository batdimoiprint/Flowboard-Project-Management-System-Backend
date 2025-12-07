/*
 * DEPRECATED: This file has been replaced by:
 * - MainTasksController.cs (handles MainTask endpoints at /api/maintasks)
 * - SubTasksController.cs (handles SubTask endpoints at /api/subtasks)
 *
 * The entity structure has been refactored as follows:
 * - Task -> SubTask (child entity with detailed information)
 * - DetailedTask -> MainTask (parent entity with schedule info: StartDate, EndDate, Status)
 *
 * Please use the new controllers instead.
 * This file can be safely deleted once all references are updated.
 */
